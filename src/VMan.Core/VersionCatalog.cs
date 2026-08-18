using System.Text.Json;

namespace VMan.Core;

/// <summary>설치 가능한 원격 버전 하나.</summary>
/// <param name="Id">`vman install` 에 그대로 넘길 수 있는 값.</param>
/// <param name="Group">메뉴에서 묶을 그룹 이름 (예: "3.12", "v22.x").</param>
/// <param name="Badge">"LTS" 같은 꼬리표. 없으면 null.</param>
public sealed record RemoteVersion(string Id, string Group, string? Badge);

/// <summary>
/// 도구별 설치 가능 버전 목록. 트레이 메뉴가 매번 네트워크를 때리지 않도록
/// 메모리 + 디스크 양쪽에 캐시한다.
/// </summary>
public static class VersionCatalog
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private static readonly Dictionary<string, IReadOnlyList<RemoteVersion>> Memory = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string CacheFile(ToolDef tool) =>
        Path.Combine(Layout.Downloads, $"catalog-{tool.Id}.json");

    /// <summary>설치 가능 버전 목록. 캐시가 살아있으면 네트워크를 쓰지 않는다.</summary>
    public static async Task<IReadOnlyList<RemoteVersion>> GetAsync(ToolDef tool, bool forceRefresh = false)
    {
        if (!forceRefresh && Memory.TryGetValue(tool.Id, out var cached))
            return cached;

        await Gate.WaitAsync();
        try
        {
            if (!forceRefresh && Memory.TryGetValue(tool.Id, out cached))
                return cached;

            Layout.EnsureDirectories();
            string file = CacheFile(tool);

            if (!forceRefresh && File.Exists(file)
                && DateTime.Now - File.GetLastWriteTime(file) < CacheLifetime)
            {
                try
                {
                    var disk = JsonSerializer.Deserialize<List<RemoteVersion>>(
                        await File.ReadAllTextAsync(file));
                    if (disk is { Count: > 0 })
                    {
                        Memory[tool.Id] = disk;
                        return disk;
                    }
                }
                catch (Exception) { /* 캐시가 깨졌으면 새로 받는다 */ }
            }

            var fresh = await FetchAsync(tool);
            Memory[tool.Id] = fresh;
            try { await File.WriteAllTextAsync(file, JsonSerializer.Serialize(fresh)); }
            catch (Exception) { /* 캐시 저장 실패는 무시 */ }
            return fresh;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Task<IReadOnlyList<RemoteVersion>> FetchAsync(ToolDef tool) => tool.Id switch
    {
        "node" => FetchNodeAsync(),
        "java" => FetchJavaAsync(),
        "python" => FetchPythonAsync(),
        _ => Task.FromResult<IReadOnlyList<RemoteVersion>>(Array.Empty<RemoteVersion>())
    };

    // ---------- Node.js ----------

    /// <summary>최근 메이저 8개, 메이저당 최신 15개까지.</summary>
    private static async Task<IReadOnlyList<RemoteVersion>> FetchNodeAsync()
    {
        string json = await Downloader.Http.GetStringAsync("https://nodejs.org/dist/index.json");
        using var doc = JsonDocument.Parse(json);

        string wantFile = $"win-{Downloader.Arch}-zip";
        var byMajor = new Dictionary<int, List<RemoteVersion>>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string raw = el.GetProperty("version").GetString()!.TrimStart('v');
            if (!int.TryParse(raw.Split('.')[0], out int major)) continue;

            // 이 PC에서 받을 수 있는 빌드가 있는 버전만
            bool hasBuild = el.TryGetProperty("files", out var files)
                && files.EnumerateArray().Any(f => f.GetString() == wantFile);
            if (!hasBuild) continue;

            string? badge = null;
            if (el.TryGetProperty("lts", out var lts) && lts.ValueKind == JsonValueKind.String)
                badge = lts.GetString();

            if (!byMajor.TryGetValue(major, out var list))
                byMajor[major] = list = new List<RemoteVersion>();
            if (list.Count < 15)
                list.Add(new RemoteVersion(raw, $"v{major}.x", badge));
        }

        return byMajor.OrderByDescending(kv => kv.Key)
                      .Take(8)
                      .SelectMany(kv => kv.Value)
                      .ToList();
    }

    // ---------- Java (Temurin) ----------

    private static async Task<IReadOnlyList<RemoteVersion>> FetchJavaAsync()
    {
        string json = await Downloader.Http.GetStringAsync(
            "https://api.adoptium.net/v3/info/available_releases");
        using var doc = JsonDocument.Parse(json);

        var lts = doc.RootElement.GetProperty("available_lts_releases")
            .EnumerateArray().Select(e => e.GetInt32()).ToHashSet();

        return doc.RootElement.GetProperty("available_releases")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .OrderByDescending(v => v)
            .Select(v => new RemoteVersion(v.ToString(), "Temurin", lts.Contains(v) ? "LTS" : null))
            .ToList();
    }

    // ---------- Python ----------

    /// <summary>3.9 이상 안정판 전부. 마이너 버전으로 그룹을 나눈다.</summary>
    private static async Task<IReadOnlyList<RemoteVersion>> FetchPythonAsync()
    {
        var all = await Downloader.ListPythonVersionsAsync();

        return all
            .Select(v =>
            {
                var parts = v.Split('.');
                return (Text: v,
                        Major: int.TryParse(parts.ElementAtOrDefault(0), out int a) ? a : 0,
                        Minor: int.TryParse(parts.ElementAtOrDefault(1), out int b) ? b : 0,
                        Patch: int.TryParse(parts.ElementAtOrDefault(2), out int c) ? c : 0);
            })
            .Where(v => v.Major == 3 && v.Minor >= 9)
            .OrderByDescending(v => v.Minor).ThenByDescending(v => v.Patch)
            .Select(v => new RemoteVersion(v.Text, $"{v.Major}.{v.Minor}", null))
            .ToList();
    }
}
