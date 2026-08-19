namespace VMan.Core;

/// <summary>도구 하나(python/java/node)의 메타데이터.</summary>
/// <param name="Id">명령줄에서 쓰는 짧은 식별자.</param>
/// <param name="DisplayName">사람에게 보여줄 이름.</param>
/// <param name="WindowsPathSubDirs">윈도우에서 current/{id} 아래 PATH에 넣을 상대 경로. ""는 루트 자신.</param>
/// <param name="UnixPathSubDirs">리눅스/WSL 에서 current/{id} 아래 PATH에 넣을 상대 경로.</param>
/// <param name="HomeEnvVar">JAVA_HOME 처럼 별도로 세팅해야 하는 환경변수 이름 (없으면 null).</param>
/// <param name="WindowsProbe">윈도우 배포본이 유효한지 확인할 실행 파일의 상대 경로.</param>
/// <param name="UnixProbe">리눅스 배포본이 유효한지 확인할 실행 파일의 상대 경로.</param>
/// <remarks>
/// 배포본 레이아웃이 OS마다 다르다.
///   Python : 윈도우는 루트에 python.exe + Scripts\,  리눅스는 bin/python3 하나
///   Node   : 윈도우는 루트에 node.exe,               리눅스는 bin/node
///   Java   : 양쪽 다 bin/
/// </remarks>
public sealed record ToolDef(
    string Id,
    string DisplayName,
    string[] WindowsPathSubDirs,
    string[] UnixPathSubDirs,
    string? HomeEnvVar,
    string WindowsProbe,
    string UnixProbe)
{
    public static readonly ToolDef Python = new(
        "python", "Python",
        WindowsPathSubDirs: new[] { "", "Scripts" },
        UnixPathSubDirs: new[] { "bin" },
        HomeEnvVar: null,
        WindowsProbe: "python.exe",
        UnixProbe: "bin/python3");

    public static readonly ToolDef Java = new(
        "java", "Java",
        WindowsPathSubDirs: new[] { "bin" },
        UnixPathSubDirs: new[] { "bin" },
        HomeEnvVar: "JAVA_HOME",
        WindowsProbe: "bin/java.exe",
        UnixProbe: "bin/java");

    public static readonly ToolDef Node = new(
        "node", "Node.js",
        WindowsPathSubDirs: new[] { "" },
        UnixPathSubDirs: new[] { "bin" },
        HomeEnvVar: null,
        WindowsProbe: "node.exe",
        UnixProbe: "bin/node");

    public static readonly IReadOnlyList<ToolDef> All = new[] { Python, Java, Node };

    /// <summary>현재 OS에서 PATH에 등록해야 하는 하위 폴더들.</summary>
    public string[] PathSubDirs => Platform.IsWindows ? WindowsPathSubDirs : UnixPathSubDirs;

    /// <summary>현재 OS에서 설치본 유효성을 확인할 실행 파일의 상대 경로.</summary>
    public string ProbeExe => Platform.NormalizeRelative(
        Platform.IsWindows ? WindowsProbe : UnixProbe);

    /// <summary>PATH 에서 이 도구를 호출할 때 쓰는 명령 이름들 (진단용).</summary>
    public IEnumerable<string> CommandNames => Id switch
    {
        "python" => Platform.IsWindows
            ? new[] { "python" + Platform.ExeSuffix }
            : new[] { "python3", "python" },
        "java" => new[] { "java" + Platform.ExeSuffix },
        _ => new[] { "node" + Platform.ExeSuffix }
    };

    public static ToolDef? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// vman이 사용하는 모든 경로.
/// 윈도우 기본 루트는 %LOCALAPPDATA%\vman, 리눅스/WSL 은 ~/.local/share/vman.
/// 두 환경이 같은 머신에 공존해도 루트가 달라 서로 섞이지 않는다.
/// </summary>
public static class Layout
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("VMAN_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return explicitRoot;

        if (Platform.IsWindows)
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vman");

        // 리눅스/WSL: XDG 규약을 따른다.
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(home, ".local", "share", "vman")
            : Path.Combine(xdg, "vman");
    }

    /// <summary>vman 실행 파일이 놓이는 곳. PATH에 등록된다.</summary>
    public static string Bin => Path.Combine(Root, "bin");

    /// <summary>링크(윈도우=정션, 리눅스=심볼릭 링크)들이 사는 곳. 이 경로가 PATH에 박히고 절대 바뀌지 않는다.</summary>
    public static string CurrentDir => Path.Combine(Root, "current");

    /// <summary>실제 설치본이 사는 곳.</summary>
    public static string VersionsDir => Path.Combine(Root, "versions");

    public static string Downloads => Path.Combine(Root, "downloads");

    public static string BackupDir => Path.Combine(Root, "backup");

    /// <summary>리눅스에서 rc 파일이 읽어들이는 환경설정 스크립트.</summary>
    public static string ShellEnvFile => Path.Combine(Root, "env.sh");

    /// <summary>current/python 같은 링크 경로.</summary>
    public static string CurrentLink(ToolDef tool) => Path.Combine(CurrentDir, tool.Id);

    /// <summary>versions/python 처럼 해당 도구의 버전들이 모이는 폴더.</summary>
    public static string ToolVersionsDir(ToolDef tool) => Path.Combine(VersionsDir, tool.Id);

    public static string VersionDir(ToolDef tool, string version) =>
        Path.Combine(ToolVersionsDir(tool), version);

    /// <summary>이 도구 때문에 PATH에 들어가야 하는 절대 경로 목록.</summary>
    public static IEnumerable<string> PathEntries(ToolDef tool)
    {
        string root = CurrentLink(tool);
        foreach (string sub in tool.PathSubDirs)
            yield return sub.Length == 0 ? root : Path.Combine(root, Platform.NormalizeRelative(sub));
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
