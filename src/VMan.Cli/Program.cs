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
                "setup"     => CmdSetup(args.Skip(1).ToArray()),
                "unsetup"   => CmdUnsetup(),
                "doctor"    => CmdDoctor(args.Skip(1).ToArray()),
                "env"       => CmdEnv(args.Skip(1).ToArray()),
                "reload"    => CmdReload(),
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

    private static int CmdSetup(string[] rest)
    {
        bool force = rest.Any(a => a is "--force" or "-f");
        var result = VersionManager.Setup(force);

        Console.WriteLine($"루트: {Layout.Root}");

        if (!result.Changed)
        {
            Console.WriteLine("이미 설정되어 있습니다. (--force 를 주면 처음부터 다시 적용합니다)");
            PrintActivationHint();
            return 0;
        }

        Console.WriteLine(Platform.IsWindows
            ? (force ? "사용자 PATH 맨 앞으로 vman 경로를 다시 끌어올렸습니다."
                     : "사용자 PATH에 vman 경로를 등록했습니다.")
            : "셸 설정에 vman 을 연결했습니다.");

        foreach (string file in result.TouchedFiles)
            Console.WriteLine($"  수정: {file}");
        Console.WriteLine($"  생성: {(Platform.IsWindows ? PowerShellEnv.EnvFile : Layout.ShellEnvFile)}");

        PrintActivationHint();
        return 0;
    }

    /// <summary>
    /// setup 직후 "지금 이 창"을 어떻게 할지 안내한다.
    ///
    /// 셸 함수가 이미 심겨 있으면(= vman 을 함수로 부르고 있으면) 방금 그 함수가
    /// 이 창에 반영까지 끝냈으므로 더 할 일이 없다. 그 사실을 알려 주는 것이 핵심이다.
    /// 아직 안 심긴 첫 설치라면 한 줄짜리 명령을 알려 준다.
    /// </summary>
    private static void PrintActivationHint()
    {
        var shell = ShellCode.Detect();
        Console.WriteLine();

        if (RanThroughWrapper())
        {
            Console.WriteLine("이 창에도 방금 반영했습니다. 이어서 바로 쓰시면 됩니다.");
            return;
        }

        Console.WriteLine("이 창에 지금 바로 적용하려면:");
        Console.WriteLine($"  {ShellCode.HowToApply(shell)}");
        Console.WriteLine();
        Console.WriteLine("다음에 여는 터미널부터는 자동으로 적용됩니다.");
        Console.WriteLine("그 뒤로는 `vman reload` 한 마디로 이 창을 다시 읽을 수 있습니다.");
    }

    /// <summary>셸에 심어 둔 vman 래퍼 함수를 통해 불렸는지. 함수가 VMAN_SHELL 을 세팅한다.</summary>
    private static bool RanThroughWrapper()
        => Environment.GetEnvironmentVariable("VMAN_SHELL") is { Length: > 0 };

    private static int CmdUnsetup()
    {
        Console.Write(Platform.IsWindows
            ? "PATH와 JAVA_HOME에서 vman 설정을 제거합니다. 계속할까요? (y/N) "
            : "rc 파일에서 vman 블록을 제거합니다. 계속할까요? (y/N) ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            return 0;

        foreach (string file in VersionManager.Unsetup())
            Console.WriteLine($"  수정: {file}");

        Console.WriteLine("제거했습니다. 설치본은 versions 폴더에 그대로 남아 있습니다.");
        if (!RanThroughWrapper())
        {
            Console.WriteLine();
            Console.WriteLine("이 창에서도 지금 걷어내려면:");
            Console.WriteLine($"  {ShellCode.HowToApply(ShellCode.Detect()).Replace("vman env", "vman env --revert")}");
        }
        return 0;
    }

    /// <summary>
    /// 이 셸에 vman 환경을 적용하는 코드를 표준출력으로 뱉는다. eval 되는 것이 전제다.
    /// 안내 문구는 절대 표준출력으로 내보내지 않는다 — 그대로 실행되어 버린다.
    /// </summary>
    private static int CmdEnv(string[] rest)
    {
        bool revert = rest.Any(a => a is "--revert" or "-r");
        bool reload = rest.Any(a => a is "--reload");

        ShellKind shell;
        int idx = Array.FindIndex(rest, a => a is "--shell" or "-s");
        if (idx >= 0)
        {
            if (idx + 1 >= rest.Length) return Fail("--shell 뒤에 셸 이름이 필요합니다 (posix, fish, powershell, cmd)");
            shell = ShellCode.Parse(rest[idx + 1])
                    ?? throw new ArgumentException(
                        $"알 수 없는 셸: {rest[idx + 1]} (posix, fish, powershell, cmd 중 하나)");
        }
        else
        {
            shell = ShellCode.Detect();
        }

        Console.Out.Write(revert ? ShellCode.Revert(shell)
                          : reload ? ShellCode.Reload(shell)
                          : ShellCode.Apply(shell));
        return 0;
    }

    /// <summary>
    /// 이 창의 환경을 새 터미널과 같은 상태로 되돌린다. `source ~/.zshrc` 에 해당한다.
    ///
    /// 셸에 심어 둔 vman 함수가 이 명령을 가로채 실제 적용까지 해 준다.
    /// 함수가 없으면(첫 설치 전, 또는 cmd) 직접 칠 한 줄을 알려 준다.
    /// </summary>
    private static int CmdReload()
    {
        if (RanThroughWrapper())
        {
            Console.WriteLine("이 창의 환경을 새로 읽었습니다.");
            foreach (var tool in ToolDef.All)
                Console.WriteLine($"  {tool.DisplayName,-8} {VersionManager.CurrentVersion(tool) ?? "(설정 안 됨)"}");
            return 0;
        }

        var shell = ShellCode.Detect();
        Console.WriteLine("이 창을 새로 읽으려면:");
        Console.WriteLine($"  {ShellCode.HowToApply(shell).Replace("vman env", "vman env --reload")}");
        Console.WriteLine();
        Console.WriteLine(Platform.IsWindows
            ? "PowerShell 이라면 `. $PROFILE` 도 같은 일을 합니다."
            : "`source ~/.bashrc` 도 같은 일을 합니다.");
        return 0;
    }

    /// <summary>
    /// "설치는 했는데 터미널에서 안 잡힌다"를 눈으로 확인시켜 준다.
    /// --fix 는 vman 경로를 PATH 맨 앞으로 되돌린다(자동으로 고칠 수 있는 건 그것뿐이다).
    /// </summary>
    private static int CmdDoctor(string[] rest)
    {
        bool fixing = rest.Any(a => a is "--fix" or "-f");
        if (fixing)
        {
            var result = VersionManager.Setup(force: true);
            Console.WriteLine("PATH 등록을 다시 적용했습니다.");
            foreach (string file in result.TouchedFiles) Console.WriteLine($"  수정: {file}");

            // 셸 함수를 통해 불렸으면 함수가 이어서 이 창까지 갱신한다.
            Console.WriteLine(RanThroughWrapper()
                ? "이 창에도 반영합니다."
                : $"이 창에 반영하려면:  {ShellCode.HowToApply(ShellCode.Detect()).Replace("vman env", "vman env --reload")}");
            Console.WriteLine();
        }

        var findings = Doctor.Run();
        foreach (var f in findings)
        {
            string mark = f.Level switch
            {
                DoctorLevel.Ok => "[ OK ]",
                DoctorLevel.Warn => "[주의]",
                _ => "[문제]"
            };
            Console.WriteLine($"{mark} {f.Title}");
            if (f.Detail.Length > 0)
                foreach (string line in f.Detail.Split('\n'))
                    Console.WriteLine($"       {line}");
            if (f.Fix is not null)
                foreach (string line in f.Fix.Split('\n'))
                    Console.WriteLine($"    → {line}");
        }

        int problems = findings.Count(f => f.Level == DoctorLevel.Error);
        Console.WriteLine();
        Console.WriteLine(problems == 0
            ? "문제 없음."
            : $"문제 {problems}건. 위의 → 안내를 따르세요.");

        // --fix 는 진단이 끝난 뒤에야 이 창에 반영된다. 위 결과는 반영 전 기준이다.
        if (fixing && problems > 0 && RanThroughWrapper())
            Console.WriteLine("(위 진단은 이 창에 반영하기 전 기준입니다. `vman doctor` 를 한 번 더 실행해 보세요.)");

        return problems == 0 ? 0 : 1;
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
        // PATH 문자열은 그대로고 링크만 바뀌므로 이미 열려 있는 셸에도 바로 반영된다.
        // 이것이 정션/심볼릭 링크 방식을 쓰는 이유다.
        Console.WriteLine("이미 열려 있는 터미널에도 바로 반영됩니다.");
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
            return Fail("사용법: vman import <도구> <버전이름> <경로>\n예: " + (Platform.IsWindows
                ? @"vman import python 3.12.4 ""C:\Python312"""
                : "vman import python 3.12.4 /opt/python3.12"));

        var tool = RequireTool(rest[0]);
        VersionManager.Import(tool, rest[1], rest[2]);
        Console.WriteLine($"등록 완료: {tool.DisplayName} {rest[1]} → {rest[2]}");
        AutoActivate(tool, rest[1]);
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
        if (!AutoActivate(tool, Path.GetFileName(path)))
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
        if (Platform.IsUnix) Console.WriteLine($"env.sh    : {Layout.ShellEnvFile}");
        Console.WriteLine("\nPATH 등록 항목:");
        foreach (string p in Layout.AllPathEntries()) Console.WriteLine("  " + p);
        return 0;
    }

    // ---------- 헬퍼 ----------

    /// <summary>
    /// 아직 이 도구에 지정된 버전이 없으면 방금 넣은 것을 바로 활성화한다.
    /// 설치/등록만 하고 use 를 잊어 "분명 깔았는데 PATH 에서 안 보인다"가 되는 경우가 많아서다.
    /// 이미 쓰는 버전이 있으면 건드리지 않는다.
    /// </summary>
    private static bool AutoActivate(ToolDef tool, string version)
    {
        if (VersionManager.CurrentVersion(tool) is not null) return false;

        VersionManager.Use(tool, version);
        Console.WriteLine($"지정된 버전이 없어 바로 활성화했습니다: {tool.DisplayName} → {version}");
        return true;
    }

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

    private static void PrintUsage()
    {
        string importExample = Platform.IsWindows
            ? @"vman import python 3.12.4 ""C:\Python312"""
            : "vman import python 3.12.4 /opt/python3.12";

        Console.WriteLine($"""
        vman - Python / Java / Node 버전 관리자  ({(Platform.IsWindows ? "Windows" : Platform.IsWsl ? "WSL2" : "Linux")})

        초기 설정
          vman setup                        폴더 생성 + PATH 등록 (최초 1회)
          vman setup --force                PATH 순서를 vman 이 맨 앞이 되도록 되돌림
          vman doctor [--fix]               왜 PATH 에서 안 잡히는지 진단
          vman env [--shell X] [--revert]    이 셸에 적용할 코드를 출력 (eval 용)
          vman reload                       이 창의 환경을 새 터미널과 같게 다시 읽기
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
          {importExample}
                                            이미 설치된 런타임을 등록

        도구: python, java, node
        """);
    }
}
