using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace VMan.Core;

/// <summary>
/// 런타임을 재배포하지 않고 공식 배포처에서 직접 받아온다.
///  - Node.js : nodejs.org/dist            (MIT)
///  - Java    : Adoptium Temurin           (GPLv2 + Classpath Exception)
///  - Python  : python-build-standalone    (PSF, astral-sh 빌드)
/// </summary>
public static class Downloader
{
    internal static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("vman/1.0");
        return c;
    }

    /// <summary>nodejs / adoptium 이 쓰는 아키텍처 이름.</summary>
    public static string Arch => Environment.Is64BitOperatingSystem
        ? (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64")
        : "x86";

    /// <summary>python-build-standalone 이 쓰는 아키텍처 이름.</summary>
    private static string PythonArch => Arch switch
    {
        "arm64" => "aarch64",
        "x64" => "x86_64",
        _ => "i686"
    };

    // ---------- Node.js ----------

    /// <summary>nodejs.org 의 버전 인덱스에서 최신 버전 목록을 가져온다.</summary>
    public static async Task<List<string>> ListNodeVersionsAsync(bool ltsOnly = true, int take = 20)
    {
        string json = await Http.GetStringAsync("https://nodejs.org/dist/index.json");
        using var doc = JsonDocument.Parse(json);

        var result = new List<string>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            bool isLts = el.GetProperty("lts").ValueKind != JsonValueKind.False;
            if (ltsOnly && !isLts) continue;
            string v = el.GetProperty("version").GetString()!.TrimStart('v');
            result.Add(v);
            if (result.Count >= take) break;
        }
        return result;
    }

    public static async Task<string> InstallNodeAsync(string version, IProgress<string>? log = null)
    {
        version = version.TrimStart('v');
        string folder = $"node-v{version}-win-{Arch}";
        string url = $"https://nodejs.org/dist/v{version}/{folder}.zip";

        return await DownloadAndExtractAsync(ToolDef.Node, version, url, null, log);
    }

    // ---------- Java (Temurin) ----------

    /// <summary>Adoptium이 제공하는 LTS 메이저 버전 목록.</summary>
    public static async Task<List<string>> ListJavaMajorsAsync()
    {
        string json = await Http.GetStringAsync("https://api.adoptium.net/v3/info/available_releases");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("available_lts_releases")
            .EnumerateArray().Select(e => e.GetInt32().ToString()).Reverse().ToList();
    }

    /// <summary>메이저 버전(예: 21)을 주면 해당 GA 최신 JDK를 받는다.</summary>
    public static async Task<string> InstallJavaAsync(string major, IProgress<string>? log = null)
    {
        string url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/" +
                     $"{Arch}/jdk/hotspot/normal/eclipse";

        return await DownloadAndExtractAsync(ToolDef.Java, $"temurin-{major}", url, null, log);
    }

    // ---------- Python (python-build-standalone) ----------

    /// <summary>
    /// astral 이 관리하는 배포 인덱스. 버전별 다운로드 URL과 SHA256이 들어있다.
    /// 3MB 남짓이라 하루 동안 캐시해서 재사용한다.
    /// </summary>
    private const string PythonIndexUrl =
        "https://raw.githubusercontent.com/astral-sh/uv/main/crates/uv-python/download-metadata.json";

    private static async Task<JsonDocument> GetPythonIndexAsync(IProgress<string>? log)
    {
        Layout.EnsureDirectories();
        string cache = Path.Combine(Layout.Downloads, "python-index.json");

        bool fresh = File.Exists(cache)
                     && DateTime.Now - File.GetLastWriteTime(cache) < TimeSpan.FromHours(24);

        if (!fresh)
        {
            log?.Report("Python 배포 목록을 갱신하는 중...");
            try
            {
                string json = await Http.GetStringAsync(PythonIndexUrl);
                await File.WriteAllTextAsync(cache, json);
            }
            catch (Exception) when (File.Exists(cache))
            {
                // 네트워크가 안 되면 오래된 캐시라도 쓴다
                log?.Report("갱신 실패 - 캐시된 목록을 사용합니다.");
            }
        }

        return JsonDocument.Parse(await File.ReadAllTextAsync(cache));
    }

    /// <summary>이 PC에서 설치 가능한 CPython 안정판 목록(오름차순).</summary>
    public static async Task<List<string>> ListPythonVersionsAsync(IProgress<string>? log = null)
    {
        using var doc = await GetPythonIndexAsync(log);

        var versions = new List<(int Major, int Minor, int Patch, string Text)>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!TryReadPythonEntry(prop, out var v, out _, out _)) continue;
            versions.Add(v);
        }

        return versions
            .OrderBy(v => v.Major).ThenBy(v => v.Minor).ThenBy(v => v.Patch)
            .Select(v => v.Text)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 인덱스 항목 하나를 읽는다.
    /// 이 PC에 맞는 windows/{arch} CPython 안정판이 아니면 false.
    /// </summary>
    private static bool TryReadPythonEntry(
        JsonProperty prop,
        out (int Major, int Minor, int Patch, string Text) version,
        out string url,
        out string sha256)
    {
        version = default; url = ""; sha256 = "";

        if (!prop.Name.StartsWith("cpython-", StringComparison.Ordinal)) return false;
        if (!prop.Name.EndsWith($"-windows-{PythonArch}-none", StringComparison.Ordinal)) return false;

        var e = prop.Value;

        // 릴리스 후보(rc/a/b)는 제외
        if (e.TryGetProperty("prerelease", out var pre)
            && pre.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(pre.GetString()))
            return false;

        if (!e.TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            return false;

        int major = e.GetProperty("major").GetInt32();
        int minor = e.GetProperty("minor").GetInt32();
        int patch = e.GetProperty("patch").GetInt32();

        version = (major, minor, patch, $"{major}.{minor}.{patch}");
        url = urlEl.GetString()!;
        sha256 = e.TryGetProperty("sha256", out var sh) && sh.ValueKind == JsonValueKind.String
            ? sh.GetString()! : "";
        return true;
    }

    /// <summary>정확한 버전(3.12.14) 또는 접두어(3.12)를 주면 해당 최신 안정판을 받는다.</summary>
    public static async Task<string> InstallPythonAsync(string version, IProgress<string>? log = null)
    {
        using var doc = await GetPythonIndexAsync(log);

        string bestVersion = "", bestUrl = "", bestSha = "";
        (int Major, int Minor, int Patch) bestNum = (-1, -1, -1);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!TryReadPythonEntry(prop, out var v, out string url, out string sha)) continue;

            bool match = v.Text == version
                         || v.Text.StartsWith(version + ".", StringComparison.Ordinal);
            if (!match) continue;

            // 접두어로 여러 개가 걸리면 가장 높은 패치를 고른다
            if ((v.Major, v.Minor, v.Patch).CompareTo(bestNum) > 0)
            {
                bestNum = (v.Major, v.Minor, v.Patch);
                bestVersion = v.Text; bestUrl = url; bestSha = sha;
            }
        }

        if (bestVersion.Length == 0)
            throw new InvalidOperationException(
                $"Python {version} 을(를) 찾을 수 없습니다. `vman available python` 으로 확인하세요.");

        return await DownloadAndExtractAsync(ToolDef.Python, bestVersion, bestUrl, bestSha, log);
    }

    // ---------- 공통 ----------

    private static async Task<string> DownloadAndExtractAsync(
        ToolDef tool, string version, string url, string? sha256, IProgress<string>? log)
    {
        Layout.EnsureDirectories();

        string dest = Layout.VersionDir(tool, version);
        if (Directory.Exists(dest))
            throw new IOException($"{tool.DisplayName} {version} 이(가) 이미 있습니다.");

        bool isTarGz = url.Contains(".tar.gz", StringComparison.OrdinalIgnoreCase);
        string ext = isTarGz ? ".tar.gz" : ".zip";
        string archivePath = Path.Combine(Layout.Downloads, $"{tool.Id}-{version}{ext}");

        log?.Report($"내려받는 중: {url}");
        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(archivePath);
            await response.Content.CopyToAsync(fs);
        }

        if (!string.IsNullOrEmpty(sha256))
        {
            log?.Report("무결성 검증 중 (SHA256)...");
            await using var fs = File.OpenRead(archivePath);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archivePath);
                throw new IOException(
                    $"SHA256이 일치하지 않습니다. 파일이 손상되었거나 변조되었을 수 있습니다.\n" +
                    $"  기대값: {sha256}\n  실제값: {actual}");
            }
        }

        string staging = Path.Combine(Layout.Downloads, $"stage-{tool.Id}-{version}");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        log?.Report("압축 푸는 중...");
        if (isTarGz)
        {
            await using var gzIn = File.OpenRead(archivePath);
            await using var gz = new GZipStream(gzIn, CompressionMode.Decompress);
            await ExtractTarAsync(gz, staging);
        }
        else
        {
            ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);
        }

        // 배포본은 대부분 단일 루트 폴더로 감싸져 있다. 한 겹 벗겨낸다.
        string source = staging;
        var entries = Directory.GetFileSystemEntries(staging);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
            source = entries[0];

        if (!File.Exists(Path.Combine(source, tool.ProbeExe)))
            throw new FileNotFoundException(
                $"압축 해제 결과에서 {tool.ProbeExe} 를 찾지 못했습니다. 아카이브 구조가 바뀌었을 수 있습니다.");

        Directory.CreateDirectory(Layout.ToolVersionsDir(tool));
        Directory.Move(source, dest);

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            File.Delete(archivePath);
        }
        catch { /* 임시 파일 정리 실패는 무시 */ }

        log?.Report($"설치 완료: {dest}");
        return dest;
    }

    /// <summary>
    /// tar 를 엔트리 단위로 직접 푼다. TarFile.ExtractToDirectory 를 쓰지 않는 이유:
    /// python-build-standalone 아카이브는 100바이트 name 필드를 재사용하면서 NUL 뒤를
    /// 0으로 지우지 않는다. POSIX 상 리더는 첫 NUL 에서 멈춰야 하는데(bsdtar 는 그렇게 한다)
    /// .NET 의 TarReader 는 잔여 바이트까지 이름에 포함시켜
    /// "python.exe" 가 "python.exe_hon.exe" 로 풀린다.
    /// </summary>
    private static async Task ExtractTarAsync(Stream tarStream, string destDir)
    {
        string destFull = Path.GetFullPath(destDir);
        Directory.CreateDirectory(destFull);

        using var reader = new TarReader(tarStream);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            string name = CleanEntryName(entry.Name);
            if (name.Length == 0) continue;

            string target = Path.GetFullPath(Path.Combine(destFull, name));

            // 아카이브가 대상 폴더 밖을 가리키는 것을 막는다 (tar-slip 방지)
            if (!target.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"아카이브 항목이 대상 폴더를 벗어납니다: {entry.Name}");

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using (var outFs = File.Create(target))
                        if (entry.DataStream is { } data)
                            await data.CopyToAsync(outFs);
                    break;

                default:
                    // 심볼릭/하드 링크, 디바이스 노드 등은 윈도우에서 필요 없다
                    break;
            }
        }
    }

    /// <summary>엔트리 이름을 첫 NUL 에서 자르고 윈도우 경로 구분자로 바꾼다.</summary>
    private static string CleanEntryName(string raw)
    {
        int nul = raw.IndexOf('\0');
        if (nul >= 0) raw = raw[..nul];
        return raw.Replace('/', Path.DirectorySeparatorChar)
                  .Trim(Path.DirectorySeparatorChar);
    }
}
