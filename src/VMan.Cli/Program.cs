using VMan.Core;

namespace VMan.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0) { PrintUsage(); return 0; }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "setup"     => CmdSetup(),
                "unsetup"   => CmdUnsetup(),
                "list" or "ls" => CmdList(args.Skip(1).ToArray()),
                "use"       => CmdUse(args.Skip(1).ToArray()),
                "unset"     => CmdUnset(args.Skip(1).ToArray()),
                "current"   => CmdCurrent(),
                "import"    => CmdImport(args.Skip(1).ToArray()),
                "remove" or "rm" => CmdRemove(args.Skip(1).ToArray()),
                "install"   => await CmdInstallAsync(args.Skip(1).ToArray()),
                "available" => await CmdAvailableAsync(args.Skip(1).ToArray()),
                "where"     => CmdWhere(),
                "help" or "-h" or "--help" => Ok(PrintUsage),
                _ => Fail($"알 수 없는 명령: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    // ---------- 명령들 ----------

    private static int CmdSetup()
    {
        bool changed = VersionManager.Setup();
        Console.WriteLine($"루트: {Layout.Root}");
        Console.WriteLine(changed
            ? "사용자 PATH에 vman 경로를 등록했습니다. 새 터미널부터 적용됩니다."
            : "PATH는 이미 설정되어 있습니다.");
        return 0;
    }

    private static int CmdUnsetup()
    {
        Console.Write("PATH와 JAVA_HOME에서 vman 설정을 제거합니다. 계속할까요? (y/N) ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            return 0;
        VersionManager.Unsetup();
        Console.WriteLine("제거했습니다. 설치본은 versions 폴더에 그대로 남아 있습니다.");
        return 0;
    }

    private static int CmdList(string[] rest)
    {
        var tools = rest.Length > 0
            ? new[] { RequireTool(rest[0]) }
            : ToolDef.All.ToArray();

        foreach (var tool in tools)
        {
            Console.WriteLine($"\n{tool.DisplayName}");
            var versions = VersionManager.List(tool);
            if (versions.Count == 0)
            {
                Console.WriteLine("  (설치된 버전 없음)");
                continue;
            }
            foreach (var v in versions)
                Console.WriteLine($"  {(v.IsCurrent ? "*" : " ")} {v.Version}");
        }
        Console.WriteLine();
        return 0;
    }

    private static int CmdUse(string[] rest)
    {
        if (rest.Length < 2) return Fail("사용법: vman use <python|java|node> <버전>");
        var tool = RequireTool(rest[0]);
        string version = ResolveVersion(tool, rest[1]);

        VersionManager.Use(tool, version);
        Console.WriteLine($"{tool.DisplayName} → {version}");
        Console.WriteLine("새로 여는 터미널부터 적용됩니다.");
        return 0;
    }

    private static int CmdUnset(string[] rest)
    {
        if (rest.Length < 1) return Fail("사용법: vman unset <python|java|node>");
        var tool = RequireTool(rest[0]);
        VersionManager.Unset(tool);
        Console.WriteLine($"{tool.DisplayName} 지정을 해제했습니다.");
        return 0;
    }

    private static int CmdCurrent()
    {
        foreach (var tool in ToolDef.All)
        {
            string? v = VersionManager.CurrentVersion(tool);
            Console.WriteLine($"{tool.DisplayName,-8} {v ?? "(설정 안 됨)",-24} {VersionManager.Probe(tool)}");
        }
        return 0;
    }

    private static int CmdImport(string[] rest)
    {
        if (rest.Length < 3)
            return Fail(@"사용법: vman import <도구> <버전이름> <경로>
예: vman import python 3.12.4 ""C:\Python312""");

        var tool = RequireTool(rest[0]);
        VersionManager.Import(tool, rest[1], rest[2]);
        Console.WriteLine($"등록 완료: {tool.DisplayName} {rest[1]} → {rest[2]}");
        return 0;
    }

    private static int CmdRemove(string[] rest)
    {
        if (rest.Length < 2) return Fail("사용법: vman remove <도구> <버전>");
        var tool = RequireTool(rest[0]);
        VersionManager.Remove(tool, rest[1]);
        Console.WriteLine($"삭제했습니다: {tool.DisplayName} {rest[1]}");
        return 0;
    }

    private static async Task<int> CmdInstallAsync(string[] rest)
    {
        if (rest.Length < 2) return Fail("사용법: vman install <python|java|node> <버전>");
        var tool = RequireTool(rest[0]);
        var log = new Progress<string>(Console.WriteLine);

        string path = tool.Id switch
        {
            "node" => await Downloader.InstallNodeAsync(rest[1], log),
            "java" => await Downloader.InstallJavaAsync(rest[1], log),
            "python" => await Downloader.InstallPythonAsync(rest[1], log),
            _ => throw new NotSupportedException()
        };

        Console.WriteLine($"설치 위치: {path}");
        Console.WriteLine($"활성화하려면: vman use {tool.Id} {Path.GetFileName(path)}");
        return 0;
    }

    private static async Task<int> CmdAvailableAsync(string[] rest)
    {
        if (rest.Length < 1) return Fail("사용법: vman available <python|java|node>");
        var tool = RequireTool(rest[0]);
        var log = new Progress<string>(Console.Error.WriteLine);

        var list = tool.Id switch
        {
            "node" => await Downloader.ListNodeVersionsAsync(),
            "java" => await Downloader.ListJavaMajorsAsync(),
            "python" => await Downloader.ListPythonVersionsAsync(log),
            _ => throw new NotSupportedException()
        };

        foreach (string v in list) Console.WriteLine("  " + v);
        return 0;
    }

    private static int CmdWhere()
    {
        Console.WriteLine($"루트      : {Layout.Root}");
        Console.WriteLine($"current   : {Layout.CurrentDir}");
        Console.WriteLine($"versions  : {Layout.VersionsDir}");
        Console.WriteLine("\nPATH 등록 항목:");
        foreach (string p in Layout.AllPathEntries()) Console.WriteLine("  " + p);
        return 0;
    }

    // ---------- 헬퍼 ----------

    private static ToolDef RequireTool(string id) =>
        ToolDef.Find(id) ?? throw new ArgumentException($"알 수 없는 도구: {id} (python, java, node 중 하나)");

    /// <summary>"21" 처럼 부분만 줘도 설치된 것 중 가장 최신으로 맞춰준다.</summary>
    private static string ResolveVersion(ToolDef tool, string input)
    {
        var installed = VersionManager.List(tool);
        var exact = installed.FirstOrDefault(v =>
            string.Equals(v.Version, input, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Version;

        var partial = installed.FirstOrDefault(v =>
            v.Version.StartsWith(input, StringComparison.OrdinalIgnoreCase) ||
            v.Version.Contains("-" + input, StringComparison.OrdinalIgnoreCase));
        if (partial is not null) return partial.Version;

        throw new ArgumentException(
            $"{tool.DisplayName} 에서 '{input}' 에 해당하는 설치본이 없습니다. `vman list {tool.Id}` 로 확인하세요.");
    }

    private static int Ok(Action a) { a(); return 0; }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("오류: " + message);
        return 1;
    }

    private static void PrintUsage() => Console.WriteLine("""
        vman - 윈도우용 Python / Java / Node 버전 관리자

        초기 설정
          vman setup                        폴더 생성 + 사용자 PATH 등록 (최초 1회)
          vman unsetup                      PATH / JAVA_HOME 원복
          vman where                        경로와 PATH 등록 항목 확인

        버전 관리
          vman list [도구]                  설치된 버전 목록 (* 가 현재 버전)
          vman current                      현재 활성 버전 + 실제 실행 결과
          vman use <도구> <버전>            버전 전환
          vman unset <도구>                 지정 해제
          vman remove <도구> <버전>         설치본 삭제

        설치
          vman available <도구>             받을 수 있는 버전 조회
          vman install node 22.5.1          Node 설치
          vman install java 21              Temurin JDK 설치
          vman install python 3.12          CPython 설치 (3.12 → 최신 패치)
          vman import python 3.12.4 "C:\Python312"
                                            이미 설치된 런타임을 등록

        도구: python, java, node
        """);
}
