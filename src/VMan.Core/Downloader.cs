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
///
/// 세 배포처 모두 윈도우용과 리눅스용을 같은 규칙으로 제공하므로,
/// OS 판별에 따라 URL 조각과 아카이브 형식만 갈아 끼우면 된다.
/// 윈도우는 zip, 리눅스는 tar.gz 를 받는다.
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

    /// <summary>nodejs.org 배포 파일 이름에 들어가는 OS 조각.</summary>
    private static string NodeOs => Platform.IsWindows ? "win" : "linux";

    /// <summary>Adoptium API 의 os 경로 조각.</summary>
    private static string AdoptiumOs => Platform.IsWindows ? "windows" : "linux";

    /// <summary>
    /// uv 인덱스 키의 꼬리표.
    ///   윈도우 : cpython-3.12.14-windows-x86_64-none
    ///   리눅스 : cpython-3.12.14-linux-x86_64-gnu
    /// 이렇게 붙여야 musl 빌드나 x86_64_v3 같은 최적화 변종이 걸리지 않는다.
    /// </summary>
    private static string PythonKeySuffix =>
        Platform.IsWindows ? $"-windows-{PythonArch}-none" : $"-linux-{PythonArch}-gnu";

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
        string folder = $"node-v{version}-{NodeOs}-{Arch}";
        // 윈도우 배포본만 zip, 나머지는 tar.gz 다. (linux 는 tar.xz 도 있지만
        // .NET 에 xz 디코더가 없어서 tar.gz 를 받는다.)
        string ext = Platform.IsWindows ? ".zip" : ".tar.gz";
        string url = $"https://nodejs.org/dist/v{version}/{folder}{ext}";

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
        string url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/{AdoptiumOs}/" +
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
    /// 이 PC(OS + 아키텍처)에 맞는 CPython 안정판이 아니면 false.
    /// </summary>
    private static bool TryReadPythonEntry(
        JsonProperty prop,
        out (int Major, int Minor, int Patch, string Text) version,
        out string url,
        out string sha256)
    {
        version = default; url = ""; sha256 = "";

        if (!prop.Name.StartsWith("cpython-", StringComparison.Ordinal)) return false;
        if (!prop.Name.EndsWith(PythonKeySuffix, StringComparison.Ordinal)) return false;

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

        string archivePath = Path.Combine(Layout.Downloads, $"{tool.Id}-{version}.archive");

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
        if (IsGzip(archivePath))
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
    /// 아카이브가 zip 인지 tar.gz 인지 앞 2바이트로 판별한다.
    /// 확장자로 판단할 수 없다. Adoptium 의 다운로드 URL 은
    /// .../jdk/hotspot/normal/eclipse 처럼 확장자가 없고 리다이렉트 뒤에야 실체가 드러나는데,
    /// 그 실체가 윈도우면 .zip, 리눅스면 .tar.gz 로 갈린다.
    /// </summary>
    private static bool IsGzip(string path)
    {
        using var fs = File.OpenRead(path);
        return fs.ReadByte() == 0x1F && fs.ReadByte() == 0x8B;
    }

    /// <summary>
    /// tar 를 엔트리 단위로 직접 푼다. TarFile.ExtractToDirectory 를 쓰지 않는 이유가 둘이다.
    ///
    /// 1) python-build-standalone 아카이브는 100바이트 name 필드를 재사용하면서 NUL 뒤를
    ///    0으로 지우지 않는다. POSIX 상 리더는 첫 NUL 에서 멈춰야 하는데(bsdtar 는 그렇게 한다)
    ///    .NET 의 TarReader 는 잔여 바이트까지 이름에 포함시켜
    ///    "python.exe" 가 "python.exe_hon.exe" 로 풀린다.
    /// 2) 리눅스 배포본은 심볼릭 링크(bin/python3 → python3.12, bin/npm → ../lib/...)와
    ///    실행 권한 비트에 의존한다. 둘 중 하나라도 잃으면 그냥 안 돌아간다.
    ///    윈도우에서는 둘 다 의미가 없으므로 건너뛴다.
    /// </summary>
    private static async Task ExtractTarAsync(Stream tarStream, string destDir)
    {
        string destFull = Path.GetFullPath(destDir);
        Directory.CreateDirectory(destFull);

        // 심볼릭 링크는 가리키는 대상이 아직 안 풀렸을 수 있으므로 마지막에 몰아서 만든다.
        var deferredLinks = new List<(string Path, string Target, bool Hard)>();

        using var reader = new TarReader(tarStream);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            string name = CleanEntryName(entry.Name);
            if (name.Length == 0) continue;

            string target = SafeCombine(destFull, name, entry.Name);

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    ApplyMode(target, entry.Mode);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using (var outFs = File.Create(target))
                        if (entry.DataStream is { } data)
                            await data.CopyToAsync(outFs);
                    ApplyMode(target, entry.Mode);
                    break;

                case TarEntryType.SymbolicLink when Platform.IsUnix:
                    deferredLinks.Add((target, CleanEntryName(entry.LinkName), Hard: false));
                    break;

                case TarEntryType.HardLink when Platform.IsUnix:
                    deferredLinks.Add((target, CleanEntryName(entry.LinkName), Hard: true));
                    break;

                default:
                    // 디바이스 노드, FIFO 등은 런타임 배포본에 있을 이유가 없다.
                    // 윈도우에서는 링크류도 여기로 떨어져 무시된다.
                    break;
            }
        }

        foreach (var (linkPath, linkTarget, hard) in deferredLinks)
            CreateExtractedLink(destFull, linkPath, linkTarget, hard);
    }

    /// <summary>아카이브 항목 경로가 대상 폴더 밖을 가리키지 않는지 확인하고 절대 경로로 만든다.</summary>
    private static string SafeCombine(string destFull, string relative, string rawName)
    {
        string full = Path.GetFullPath(Path.Combine(destFull, relative));

        // tar-slip 방지: "../../etc/passwd" 같은 항목을 막는다.
        if (!full.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"아카이브 항목이 대상 폴더를 벗어납니다: {rawName}");

        return full;
    }

    /// <summary>
    /// 링크를 만든다. 대상이 폴더 밖을 가리키면 거부한다.
    /// 하드링크는 .NET 에 API 가 없어서 내용을 복사한다(런타임 배포본에서는 드물다).
    /// </summary>
    private static void CreateExtractedLink(string destFull, string linkPath, string linkTarget, bool hard)
    {
        if (linkTarget.Length == 0) return;

        // 링크 대상이 어디로 풀리는지 계산한다. 상대 경로는 링크가 있는 폴더 기준.
        string resolved = Path.IsPathRooted(linkTarget)
            ? Path.GetFullPath(linkTarget)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, linkTarget));

        if (!resolved.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, destFull, StringComparison.Ordinal))
            throw new IOException($"링크가 대상 폴더 밖을 가리킵니다: {linkPath} -> {linkTarget}");

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        if (File.Exists(linkPath) || Directory.Exists(linkPath)) File.Delete(linkPath);

        if (hard)
        {
            if (File.Exists(resolved)) File.Copy(resolved, linkPath, overwrite: true);
            return;
        }

        // 상대 경로 그대로 심는다. 나중에 설치 폴더가 옮겨져도 링크가 유지된다.
        File.CreateSymbolicLink(linkPath, linkTarget);
    }

    /// <summary>tar 에 기록된 유닉스 권한을 되살린다. 실행 비트를 잃으면 런타임이 안 돈다.</summary>
    private static void ApplyMode(string path, UnixFileMode mode)
    {
        if (Platform.IsWindows || mode == UnixFileMode.None) return;
        try { File.SetUnixFileMode(path, mode); }
        catch (Exception) { /* 권한 설정 실패는 치명적이지 않다 */ }
    }

    /// <summary>엔트리 이름을 첫 NUL 에서 자르고 현재 OS 경로 구분자로 바꾼다.</summary>
    private static string CleanEntryName(string raw)
    {
        int nul = raw.IndexOf('\0');
        if (nul >= 0) raw = raw[..nul];
        return raw.Replace('/', Path.DirectorySeparatorChar)
                  .Trim(Path.DirectorySeparatorChar);
    }
}
