namespace VMan.Core;

/// <summary>도구 하나(python/java/node)의 메타데이터.</summary>
/// <param name="Id">명령줄에서 쓰는 짧은 식별자.</param>
/// <param name="DisplayName">사람에게 보여줄 이름.</param>
/// <param name="PathSubDirs">current\{id} 기준 상대 경로. PATH에 등록될 폴더들. ""는 루트 자신.</param>
/// <param name="HomeEnvVar">JAVA_HOME 처럼 별도로 세팅해야 하는 환경변수 이름 (없으면 null).</param>
/// <param name="ProbeExe">버전 폴더가 유효한지 확인할 때 존재를 검사할 실행 파일의 상대 경로.</param>
public sealed record ToolDef(
    string Id,
    string DisplayName,
    string[] PathSubDirs,
    string? HomeEnvVar,
    string ProbeExe)
{
    public static readonly ToolDef Python = new(
        "python", "Python", new[] { "", "Scripts" }, null, "python.exe");

    public static readonly ToolDef Java = new(
        "java", "Java", new[] { "bin" }, "JAVA_HOME", @"bin\java.exe");

    public static readonly ToolDef Node = new(
        "node", "Node.js", new[] { "" }, null, "node.exe");

    public static readonly IReadOnlyList<ToolDef> All = new[] { Python, Java, Node };

    public static ToolDef? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>vman이 사용하는 모든 경로. 기본 루트는 %LOCALAPPDATA%\vman.</summary>
public static class Layout
{
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("VMAN_ROOT")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vman");

    /// <summary>vman.exe / vman-tray.exe 가 놓이는 곳. PATH에 등록된다.</summary>
    public static string Bin => Path.Combine(Root, "bin");

    /// <summary>정션들이 사는 곳. 여기 경로가 PATH에 박히고 절대 바뀌지 않는다.</summary>
    public static string CurrentDir => Path.Combine(Root, "current");

    /// <summary>실제 설치본이 사는 곳.</summary>
    public static string VersionsDir => Path.Combine(Root, "versions");

    public static string Downloads => Path.Combine(Root, "downloads");

    public static string BackupDir => Path.Combine(Root, "backup");

    /// <summary>current\python 같은 정션 경로.</summary>
    public static string CurrentLink(ToolDef tool) => Path.Combine(CurrentDir, tool.Id);

    /// <summary>versions\python 처럼 해당 도구의 버전들이 모이는 폴더.</summary>
    public static string ToolVersionsDir(ToolDef tool) => Path.Combine(VersionsDir, tool.Id);

    public static string VersionDir(ToolDef tool, string version) =>
        Path.Combine(ToolVersionsDir(tool), version);

    /// <summary>이 도구 때문에 PATH에 들어가야 하는 절대 경로 목록.</summary>
    public static IEnumerable<string> PathEntries(ToolDef tool)
    {
        string root = CurrentLink(tool);
        foreach (string sub in tool.PathSubDirs)
            yield return sub.Length == 0 ? root : Path.Combine(root, sub);
    }

    /// <summary>vman이 PATH에 넣는 모든 경로 (bin 포함).</summary>
    public static IEnumerable<string> AllPathEntries()
    {
        yield return Bin;
        foreach (var tool in ToolDef.All)
            foreach (string p in PathEntries(tool))
                yield return p;
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(CurrentDir);
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(BackupDir);
        foreach (var tool in ToolDef.All)
            Directory.CreateDirectory(ToolVersionsDir(tool));
    }
}
