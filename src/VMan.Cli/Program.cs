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
                "venv"      => CmdVenv(args.Skip(1).ToArray()),
                "activate"  => CmdActivate(args.Skip(1).ToArray()),
                "deactivate"=> CmdDeactivate(),
                "menu"      => CmdMenu(args.Skip(1).ToArray()),
                "autoactivate" or "auto" => CmdAutoActivate(args.Skip(1).ToArray()),
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

        // 실행 정책이 막고 있으면 프로필을 심어 봐야 소용이 없다. 그 사실을 말하지 않으면
        // "다음 터미널부터 적용됩니다" 가 거짓말이 된다. 정책 변경은 사용자가 직접 해야 한다.
        if (Platform.IsWindows && PowerShellEnv.ExecutionPolicyBlocksProfile(out string policy))
        {
            Console.WriteLine($"주의: PowerShell 실행 정책이 {policy} 라서 프로필이 실행되지 않습니다.");
            Console.WriteLine("     이대로면 새 터미널을 열어도 vman 연동이 켜지지 않습니다.");
            Console.WriteLine();
            Console.WriteLine("  먼저 이것을 실행하세요 (관리자 권한 불필요):");
            Console.WriteLine($"    {PowerShellEnv.PolicyFixCommand}");
            Console.WriteLine();
            Console.WriteLine("  그 다음 새 터미널을 열면 됩니다.");
            return;
        }

        // 방금 setup 을 했으니 연동은 설치되어 있다. 그러면 프로필을 다시 읽는 한 줄이
        // 가장 간단하고, eval 주문은 그 다음이다.
        Console.WriteLine("다음에 여는 터미널부터 자동으로 적용됩니다.");
        Console.WriteLine();
        Console.WriteLine("이 창에 지금 적용하려면:");
        Console.WriteLine($"  {EnvStore.SourceProfileCommand()}");
        Console.WriteLine();
        Console.WriteLine($"그래도 안 되면:  {ShellCode.HowToApply(shell)}");
        Console.WriteLine("적용된 뒤로는 `vman reload` 한 마디면 됩니다.");
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

    // ---------- 가상환경 ----------

    /// <summary>
    /// 현재 디렉터리(또는 --dir)에 가상환경을 만든다.
    /// 셸 함수가 있으면 만든 즉시 이 창에서 활성화까지 된다.
    /// </summary>
    private static int CmdVenv(string[] rest)
    {
        string dir = Environment.CurrentDirectory;
        int d = Array.FindIndex(rest, a => a is "--dir" or "-d");
        if (d >= 0)
        {
            if (d + 1 >= rest.Length) return Fail("--dir 뒤에 폴더 경로가 필요합니다.");
            dir = rest[d + 1];
            rest = rest.Where((_, i) => i != d && i != d + 1).ToArray();
        }

        string name = rest.FirstOrDefault(a => !a.StartsWith('-')) ?? VenvManager.DefaultName;

        // 이미 있으면 새로 만들지 않고 그것을 쓴다. 사용자가 원하는 것은 대개
        // "이 폴더의 가상환경을 켜자" 이지 "무조건 새로 만들어라" 가 아니다.
        var venv = VenvManager.Ensure(dir, name, out bool created,
                                      new Progress<string>(Console.WriteLine));
        Console.WriteLine($"파이썬: {VenvManager.Probe(venv)}");

        if (RanThroughWrapper())
        {
            Console.WriteLine(created
                ? "이 창에서 활성화했습니다. 이제 pip 은 여기에만 설치합니다."
                : "이 창에서 활성화했습니다.");
            return 0;
        }

        PrintActivateHelp();
        return 0;
    }

    /// <summary>
    /// 활성화 방법 안내. 사람이 칠 명령(`vman activate`)을 앞세운다.
    /// 여기까지 왔다는 것은 셸 연동이 없다는 뜻이므로 그 사실도 같이 알린다.
    /// </summary>
    private static void PrintActivateHelp()
    {
        Console.WriteLine();
        Console.WriteLine("활성화하려면:");
        Console.WriteLine("  vman activate");
        PrintWrapperMissing();
    }

    /// <summary>이 폴더(또는 위쪽)에 있는 가상환경을 활성화한다.</summary>
    private static int CmdActivate(string[] rest)
    {
        string start = rest.FirstOrDefault(a => !a.StartsWith('-')) ?? Environment.CurrentDirectory;

        var venv = (VenvManager.Resolve(Environment.CurrentDirectory, start)
                    ?? VenvManager.Find(start))
                   ?? throw new DirectoryNotFoundException(
                       $"{Path.GetFullPath(start)} 및 상위 폴더에서 가상환경을 찾지 못했습니다. "
                       + "`vman venv` 로 만드세요.");

        Console.WriteLine($"가상환경: {venv.Path}");
        Console.WriteLine($"파이썬  : {VenvManager.Probe(venv)}");

        if (!RanThroughWrapper()) PrintWrapperMissing();
        return 0;
    }

    /// <summary>
    /// 이 창에 적용되지 못했을 때의 안내.
    ///
    /// eval 주문만 던지면 사용자는 그것이 사용법인 줄 안다. 그것은 우회책이다.
    /// 게다가 상황이 둘로 갈리고 해결책이 서로 다르다.
    ///   연동이 아예 없음        → vman setup
    ///   창이 연동보다 먼저 열림 → 새 터미널, 또는 지금 창이면 . $PROFILE
    /// 원인을 먼저 말하고 그 상황에 맞는 해결책을 앞세운다.
    /// </summary>
    /// <param name="evalCommand">최후의 수단으로 보여줄 eval 한 줄.</param>
    private static void PrintNotAppliedHere(string evalCommand)
    {
        Console.WriteLine();
        if (!EnvStore.ShellIntegrationInstalled())
        {
            Console.WriteLine("이 창에는 적용되지 않았습니다 — 셸 연동이 설치되어 있지 않습니다.");
            Console.WriteLine("프로세스는 자기를 부른 셸의 환경을 바꿀 수 없어서, vman 이 셸 안에");
            Console.WriteLine("함수로 심겨 있어야 합니다.");
            Console.WriteLine();
            Console.WriteLine("한 번만 해두면 됩니다:");
            Console.WriteLine("  vman setup        그리고 새 터미널을 여세요");
        }
        else if (Platform.IsWindows && PowerShellEnv.ExecutionPolicyBlocksProfile(out string policy))
        {
            // 프로필은 심겨 있지만 실행 정책이 막고 있다. 이 경우 새 터미널도,
            // . $PROFILE 도 소용없다. 정책부터 풀어야 한다.
            Console.WriteLine($"이 창에는 적용되지 않았습니다 — PowerShell 실행 정책이 {policy} 입니다.");
            Console.WriteLine("프로필에 vman 연동은 심겨 있지만 실행되지 못합니다.");
            Console.WriteLine("새 터미널을 열어도 마찬가지입니다.");
            Console.WriteLine();
            Console.WriteLine("한 번만 풀어 주면 됩니다 (관리자 권한 불필요):");
            Console.WriteLine($"  {PowerShellEnv.PolicyFixCommand}");
            Console.WriteLine();
            Console.WriteLine("그 다음 새 터미널을 여세요.");
        }
        else
        {
            Console.WriteLine("이 창에는 적용되지 않았습니다 — 이 창이 셸 연동보다 먼저 열렸습니다.");
            Console.WriteLine();
            Console.WriteLine("새 터미널을 열면 됩니다. 이 창을 그대로 쓰려면:");
            Console.WriteLine($"  {EnvStore.SourceProfileCommand()}");
        }

        Console.WriteLine();
        Console.WriteLine("이 창에 이번만 적용하려면:");
        Console.WriteLine($"  {evalCommand}");
    }

    private static void PrintWrapperMissing() => PrintNotAppliedHere(ActivateHint());

    private static int CmdDeactivate()
    {
        var active = VenvManager.Active();
        Console.WriteLine(active is null
            ? "활성화된 가상환경이 없습니다."
            : $"가상환경을 해제했습니다: {active.Path}");

        if (!RanThroughWrapper())
        {
            Console.WriteLine();
            Console.WriteLine("이 창에 적용하려면:");
            Console.WriteLine($"  {ShellCode.HowToApply(ShellCode.Detect()).Replace("vman env", "vman env --deactivate")}");
        }
        return 0;
    }

    private static string ActivateHint()
        => ShellCode.HowToApply(ShellCode.Detect()).Replace("vman env", "vman env --activate");

    /// <summary>
    /// 폴더를 옮길 때 가상환경을 자동으로 켜고 끌지 설정한다.
    ///
    /// 설정은 settings.json 에 저장되어 다음 셸부터 적용되고, 셸 함수가 있으면
    /// 지금 이 창의 VMAN_AUTO_VENV 까지 바꿔 즉시 반영된다.
    /// </summary>
    private static int CmdAutoActivate(string[] rest)
    {
        var settings = Settings.Load();
        string? arg = rest.FirstOrDefault()?.ToLowerInvariant();

        if (arg is null)
        {
            Console.WriteLine($"자동활성화: {(settings.AutoActivateVenv ? "켜짐" : "꺼짐")}");
            Console.WriteLine("바꾸려면: vman autoactivate <on|off>");
            return 0;
        }

        bool? wanted = arg switch
        {
            "on" or "true" or "1" or "켜기" => true,
            "off" or "false" or "0" or "끄기" => false,
            _ => null
        };
        if (wanted is null) return Fail("사용법: vman autoactivate <on|off>");

        settings.AutoActivateVenv = wanted.Value;
        settings.Save();

        // 다음 셸이 읽을 파일도 같이 갱신한다.
        if (Platform.IsWindows) PowerShellEnv.WriteEnvFile();
        else ShellEnv.WriteEnvFile();

        Console.WriteLine($"자동활성화: {(wanted.Value ? "켜짐" : "꺼짐")}");
        Console.WriteLine(RanThroughWrapper()
            ? "이 창에도 바로 적용했습니다."
            : "새 셸부터 적용됩니다.");
        return 0;
    }

    /// <summary>탐색기 우클릭 메뉴 등록/해제. 윈도우 전용.</summary>
    private static int CmdMenu(string[] rest)
    {
        if (!Platform.IsWindows)
            return Fail("탐색기 우클릭 메뉴는 윈도우 전용입니다.");

        string action = rest.FirstOrDefault() ?? "status";
        switch (action)
        {
            case "install":
                ExplorerMenu.Install();
                Console.WriteLine("탐색기 우클릭 메뉴를 등록했습니다.");
                Console.WriteLine("폴더를 우클릭하면 「vman 가상환경 만들기」가 보입니다.");
                Console.WriteLine();
                Console.WriteLine("윈도우 11 은 이런 항목을 「추가 옵션 표시」(Shift+F10) 안쪽에 넣습니다.");
                return 0;

            case "uninstall":
                ExplorerMenu.Uninstall();
                Console.WriteLine("탐색기 우클릭 메뉴를 제거했습니다.");
                return 0;

            case "status":
                Console.WriteLine(ExplorerMenu.IsInstalled()
                    ? "등록되어 있습니다."
                    : "등록되어 있지 않습니다. `vman menu install` 로 등록하세요.");
                return 0;

            default:
                return Fail("사용법: vman menu <install|uninstall|status>");
        }
    }

    /// <summary>
    /// 이 셸에 vman 환경을 적용하는 코드를 표준출력으로 뱉는다. eval 되는 것이 전제다.
    /// 안내 문구는 절대 표준출력으로 내보내지 않는다 — 그대로 실행되어 버린다.
    /// </summary>
    private static int CmdEnv(string[] rest)
    {
        bool revert = rest.Any(a => a is "--revert" or "-r");
        bool reload = rest.Any(a => a is "--reload");
        bool activate = rest.Any(a => a is "--activate");
        bool deactivate = rest.Any(a => a is "--deactivate");
        bool autoFlag = rest.Any(a => a is "--auto");

        ShellKind shell;
        int idx = Array.FindIndex(rest, a => a is "--shell" or "-s");
        if (idx >= 0)
        {
            if (idx + 1 >= rest.Length) return Fail("--shell 뒤에 셸 이름이 필요합니다 (posix, fish, powershell, cmd)");
            shell = ShellCode.Parse(rest[idx + 1])
                    ?? throw new ArgumentException(
                        $"알 수 없는 셸: {rest[idx + 1]} (posix, fish, powershell, cmd 중 하나)");

            // --shell 의 값을 여기서 떼어낸다. 안 그러면 그 값("posix")이
            // 아래에서 위치 인자로 잡혀 가상환경 이름으로 해석된다.
            rest = rest.Where((_, i) => i != idx && i != idx + 1).ToArray();
        }
        else
        {
            shell = ShellCode.Detect();
        }

        // 자동활성화 스위치만 이 셸에 반영한다. 설정을 바꾼 직후 래퍼가 부른다.
        if (autoFlag)
        {
            string v = Settings.Load().AutoActivateVenv ? "1" : "0";
            Console.Out.Write(shell switch
            {
                ShellKind.PosixShell => $"export VMAN_AUTO_VENV={v}\n",
                ShellKind.Fish => $"set -gx VMAN_AUTO_VENV {v}\n",
                ShellKind.PowerShell => $"$env:VMAN_AUTO_VENV = '{v}'\n",
                _ => $"set \"VMAN_AUTO_VENV={v}\"\n"
            });
            return 0;
        }

        if (activate)
        {
            // 대상을 명시할 수 있다. `vman venv .venv` 처럼 방금 만든 것을 정확히 켜야 하는데,
            // 이름을 안 주면 Find 가 고정 순서로 골라서 엉뚱한 것이 켜진다.
            string? target = rest.FirstOrDefault(a => a.Length > 0 && !a.StartsWith('-'));

            var venv = target is null
                ? VenvManager.Find(Environment.CurrentDirectory)
                : VenvManager.Resolve(Environment.CurrentDirectory, target);

            // eval 되는 자리라 실패해도 표준출력은 비워 둔다. 안내는 표준오류로.
            if (venv is null)
            {
                Console.Error.WriteLine("가상환경을 찾지 못했습니다. `vman venv` 로 만드세요.");
                return 1;
            }
            Console.Out.Write(ShellCode.Activate(shell, venv));
            return 0;
        }

        Console.Out.Write(deactivate ? ShellCode.Deactivate(shell)
                          : revert ? ShellCode.Revert(shell)
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

        // reload 는 셸 연동이 있어야 동작한다. 여기까지 왔다는 것은 그것이 없다는 뜻이고,
        // 그러면 reload 자신이 이 창을 고칠 수 없다. 무엇이 빠졌는지 말해 준다.
        var shell = ShellCode.Detect();
        PrintNotAppliedHere(
            ShellCode.HowToApply(shell).Replace("vman env", "vman env --reload"));
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

        // 프롬프트를 못 보는 자리(스크립트, cmd, 편집기 터미널)에서도
        // 가상환경이 켜졌는지 확인할 수 있어야 한다.
        Console.WriteLine();
        string? active = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        if (string.IsNullOrWhiteSpace(active))
        {
            Console.WriteLine("가상환경  (꺼짐)");
            var here = VenvManager.Find(Environment.CurrentDirectory);
            if (here is not null)
                Console.WriteLine($"          이 폴더에 {here.Name} 이(가) 있습니다. `vman activate` 로 켜세요.");
        }
        else
        {
            Console.WriteLine($"가상환경  {active}");
            Console.WriteLine($"          {VenvManager.Probe(new Venv(active, Path.GetFileName(active.TrimEnd('\\', '/'))))}");
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
          vman env [--shell X] [--revert]   이 셸에 적용할 코드를 출력 (eval 용)
          vman reload                       이 창의 환경을 새 터미널과 같게 다시 읽기
          vman unsetup                      PATH / JAVA_HOME 원복
          vman where                        경로와 PATH 등록 항목 확인

        가상환경 (폴더별 pip 격리)
          vman venv [이름]                  이 폴더에 가상환경 생성 (기본 .venv)
          vman activate                     이 폴더의 가상환경을 이 창에 적용
          vman deactivate                   가상환경 해제
          vman autoactivate [on|off]        폴더 이동 시 자동 활성화 (기본 켜짐)
          vman menu install                 탐색기 우클릭 메뉴 등록 (윈도우)

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
