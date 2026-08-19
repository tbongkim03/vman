using System.Runtime.Versioning;

namespace VMan.Core;

public enum DoctorLevel { Ok, Warn, Error }

/// <summary>진단 결과 한 줄.</summary>
/// <param name="Level">심각도.</param>
/// <param name="Title">한 줄 요약.</param>
/// <param name="Detail">근거가 되는 실제 값(경로 등).</param>
/// <param name="Fix">사용자가 해야 할 일. 문제가 없으면 null.</param>
public sealed record DoctorFinding(DoctorLevel Level, string Title, string Detail = "", string? Fix = null);

/// <summary>
/// "설치는 했는데 터미널에서 안 잡힌다"를 눈으로 확인시켜 주는 진단기.
///
/// 이 도구가 필요한 이유는 하나다. PATH 는 먼저 걸리는 놈이 이기는 구조라서,
/// vman 이 제 할 일을 다 해도 다른 무언가가 앞줄에 서 있으면 그냥 가려진다.
/// 대표적인 두 가지:
///   낡은 창 - `vman setup` 은 레지스트리를 고칠 뿐, 이미 열려 있던 터미널은 시작할 때
///             복사해 둔 환경 블록을 계속 쓴다. 그 창에서는 vman 경로가 아예 없는 것과 같다.
///             가장 흔하고, 가장 알아채기 어려운 실패다. 레지스트리만 보면 멀쩡해 보인다.
///   앱 실행 별칭 - 윈도우가 기본으로 심어 두는 WindowsApps\python.exe (내용이 없는 스텁).
///             위처럼 vman 경로가 빠진 상태면 이놈이 대신 잡혀 스토어 설치를 권한다.
///   WSL2   - 윈도우 PATH 가 그대로 딸려 들어와(interop) /mnt/c/... 아래 실행 파일이 먼저 잡힌다.
/// </summary>
public static class Doctor
{
    public static List<DoctorFinding> Run()
    {
        var findings = new List<DoctorFinding>();

        findings.Add(new DoctorFinding(DoctorLevel.Ok,
            $"플랫폼: {(Platform.IsWindows ? "Windows" : Platform.IsWsl ? "WSL2 (리눅스)" : "Linux")}",
            $"루트 {Layout.Root}"));

        CheckRoot(findings);
        CheckPathRegistration(findings);
        CheckStaleSession(findings);
        CheckShellIntegration(findings);

        foreach (var tool in ToolDef.All)
            CheckTool(findings, tool);

        if (Platform.IsWindows) CheckStoreAliases(findings);
        if (Platform.IsWsl) CheckWslInterop(findings);

        return findings;
    }

    // ---------- 개별 검사 ----------

    private static void CheckRoot(List<DoctorFinding> findings)
    {
        if (Directory.Exists(Layout.CurrentDir) && Directory.Exists(Layout.VersionsDir)) return;

        findings.Add(new DoctorFinding(DoctorLevel.Error,
            "vman 폴더 구조가 없습니다",
            Layout.Root,
            "vman setup 을 실행하세요."));
    }

    private static void CheckPathRegistration(List<DoctorFinding> findings)
    {
        var effective = EnvStore.EffectivePathEntries();
        var missing = Layout.AllPathEntries()
            .Where(e => !effective.Any(p => SamePath(p, e)))
            .ToList();

        if (missing.Count == 0)
        {
            findings.Add(new DoctorFinding(DoctorLevel.Ok, "PATH 에 vman 경로가 모두 등록되어 있습니다"));
            return;
        }

        findings.Add(new DoctorFinding(DoctorLevel.Error,
            $"PATH 에 vman 경로 {missing.Count}개가 빠져 있습니다"
            + (Platform.IsWindows ? " (레지스트리 기준)" : ""),
            string.Join("\n", missing),
            $"vman setup 을 실행한 뒤:  {ReloadHint()}\n"
            + "    (창을 새로 열어도 됩니다.)"));
    }

    /// <summary>
    /// 레지스트리에는 등록됐는데 <b>지금 이 창</b>에는 반영되지 않은 상태를 잡는다.
    ///
    /// 이것이 윈도우에서 가장 흔한 실패다. `vman setup` 은 레지스트리를 고칠 뿐이고,
    /// 이미 열려 있던 터미널은 시작 시점에 복사한 환경 블록을 끝까지 쓴다.
    /// 그 창에서 python 을 치면 vman 경로가 없는 것과 똑같이 동작해서,
    /// 윈도우가 기본으로 심어 둔 WindowsApps\python.exe 스텁이 대신 잡히고
    /// "스토어에서 파이썬을 설치하라"는 안내가 뜬다.
    ///
    /// 레지스트리만 검사하면 전부 [OK] 로 보이기 때문에 따로 짚어 줘야 한다.
    /// </summary>
    private static void CheckStaleSession(List<DoctorFinding> findings)
    {
        if (!Platform.IsWindows) return;

        // 레지스트리에 제대로 들어가 있을 때만 의미가 있는 검사다.
        var registry = EnvStore.EffectivePathEntries();
        if (Layout.AllPathEntries().Any(e => !registry.Any(p => SamePath(p, e)))) return;

        var session = EnvStore.SessionPathEntries();
        var missing = Layout.AllPathEntries()
            .Where(e => !session.Any(p => SamePath(p, e)))
            .ToList();

        if (missing.Count == 0)
        {
            findings.Add(new DoctorFinding(DoctorLevel.Ok,
                "이 터미널에도 vman 경로가 반영되어 있습니다"));
            return;
        }

        findings.Add(new DoctorFinding(DoctorLevel.Error,
            "이 터미널은 vman 설정보다 먼저 열린 창입니다",
            "레지스트리에는 vman 경로가 들어 있지만 이 창의 환경에는 없습니다.\n"
            + "프로세스는 시작할 때 환경 블록을 복사해서 쓰기 때문입니다.\n"
            + $"이 창에 없는 경로: {missing.Count}개",
            $"이 창을 그 자리에서 고치려면:  {ReloadHint()}\n"
            + "    창을 새로 열어도 됩니다. 둘 다 결과는 같습니다.\n"
            + "    (이 창에서 python 을 치면 스토어 설치 안내가 뜨는 것이 바로 이 때문입니다.)"));
    }

    /// <summary>rc 파일(리눅스) 또는 프로필(윈도우)에 vman 블록이 심겨 있는지.</summary>
    private static void CheckShellIntegration(List<DoctorFinding> findings)
    {
        if (Platform.IsWindows) { CheckPowerShellIntegration(findings); return; }

        if (!File.Exists(Layout.ShellEnvFile))
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                "env.sh 가 없습니다", Layout.ShellEnvFile, "vman setup 을 실행하세요."));
            return;
        }

        var rcFiles = ShellEnv.InstalledRcFiles();
        if (rcFiles.Count == 0)
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                "어떤 rc 파일에도 vman 블록이 없습니다",
                "~/.bashrc, ~/.zshrc, ~/.profile 을 확인했습니다.",
                "vman setup 을 실행하세요."));
            return;
        }

        findings.Add(new DoctorFinding(DoctorLevel.Ok,
            "셸 설정이 연결되어 있습니다", string.Join(", ", rcFiles)));
    }

    /// <summary>
    /// 윈도우: env.ps1 과 $PROFILE 연동.
    ///
    /// 이게 없으면 PATH 는 멀쩡한데 `vman use` / `vman activate` 가 지금 창에 먹지 않는다.
    /// 프로세스는 자기를 부른 셸의 환경을 바꿀 수 없어서 셸 안에 함수가 있어야 하는데,
    /// 그 사실이 겉으로 드러나지 않아 "명령은 성공했다는데 아무 일도 안 난다" 로 보인다.
    /// 셸 연동이 없던 시절에 setup 을 한 뒤 vman 만 갱신하면 정확히 이 상태가 된다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void CheckPowerShellIntegration(List<DoctorFinding> findings)
    {
        if (!File.Exists(PowerShellEnv.EnvFile))
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                "셸 연동이 설치되어 있지 않습니다 (env.ps1 없음)",
                PowerShellEnv.EnvFile,
                "vman setup 을 실행한 뒤 새 터미널을 여세요."));
            return;
        }

        var profiles = PowerShellEnv.InstalledProfiles();
        if (profiles.Count == 0)
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                "PowerShell 프로필에 vman 블록이 없습니다",
                "env.ps1 은 있지만 프로필이 그것을 읽지 않습니다.",
                "vman setup 을 실행한 뒤 새 터미널을 여세요."));
            return;
        }

        // 프로필을 잘 심어 놨어도 실행 정책이 막으면 아무 소용이 없다.
        // 윈도우 기본값이 Restricted 라서 아무것도 안 건드린 PC 가 여기 해당한다.
        if (PowerShellEnv.ExecutionPolicyBlocksProfile(out string policy))
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                $"PowerShell 실행 정책({policy})이 프로필 로드를 막고 있습니다",
                "프로필에 vman 블록은 있지만 실행되지 않습니다. 새 터미널을 열어도 마찬가지입니다.",
                PowerShellEnv.PolicyFixCommand + "   (관리자 권한 불필요)"));
            return;
        }

        if (!ViaWrapper())
        {
            findings.Add(new DoctorFinding(DoctorLevel.Warn,
                "이 창에는 셸 연동이 적용되어 있지 않습니다",
                "설정은 되어 있으나 이 창이 그보다 먼저 열렸습니다.",
                "새 터미널을 열거나 `vman reload` 를 실행하세요."));
            return;
        }

        findings.Add(new DoctorFinding(DoctorLevel.Ok,
            "셸 연동이 연결되어 있습니다", string.Join(", ", profiles)));
    }

    private static void CheckTool(List<DoctorFinding> findings, ToolDef tool)
    {
        string link = Layout.CurrentLink(tool);
        string? version = VersionManager.CurrentVersion(tool);
        var installed = VersionManager.List(tool);

        if (version is null)
        {
            var level = installed.Count > 0 ? DoctorLevel.Warn : DoctorLevel.Ok;
            findings.Add(new DoctorFinding(level,
                $"{tool.DisplayName}: 사용할 버전이 지정되어 있지 않습니다",
                installed.Count > 0
                    ? $"설치된 버전: {string.Join(", ", installed.Select(v => v.Version))}"
                    : "설치된 버전이 없습니다.",
                installed.Count > 0
                    ? $"vman use {tool.Id} {installed[0].Version}"
                    : null));
        }
        else if (!File.Exists(Path.Combine(link, tool.ProbeExe)))
        {
            findings.Add(new DoctorFinding(DoctorLevel.Error,
                $"{tool.DisplayName}: 링크가 깨졌습니다",
                $"{link} → {Links.GetTarget(link)} (에서 {tool.ProbeExe} 를 찾을 수 없음)",
                $"vman use {tool.Id} <버전> 으로 다시 지정하세요."));
        }
        else
        {
            findings.Add(new DoctorFinding(DoctorLevel.Ok,
                $"{tool.DisplayName}: {version}", link));
        }

        CheckShadowing(findings, tool);
    }

    /// <summary>PATH 에서 이 도구의 명령이 실제로 어디서 잡히는지 확인한다.</summary>
    private static void CheckShadowing(List<DoctorFinding> findings, ToolDef tool)
    {
        // vman 이 아직 이 도구를 맡고 있지 않으면 가려질 것도 없다.
        // (시스템 java 가 잡히는 건 vman 에게 java 를 맡기지 않았으니 당연한 일이다.)
        if (VersionManager.CurrentVersion(tool) is null) return;

        var effective = EnvStore.EffectivePathEntries();
        var vmanEntries = Layout.PathEntries(tool).ToList();

        foreach (string command in tool.CommandNames)
        {
            string? resolved = ResolveCommand(effective, command);
            if (resolved is null) continue;

            string dir = Path.GetDirectoryName(resolved)!;
            if (vmanEntries.Any(e => SamePath(e, dir))) continue;   // vman 것이 이겼다

            findings.Add(new DoctorFinding(DoctorLevel.Warn,
                $"{command} 이(가) vman 이 아닌 다른 곳에서 잡힙니다",
                resolved,
                DescribeShadowFix(resolved)));
        }
    }

    private static string DescribeShadowFix(string resolvedPath)
    {
        if (Platform.IsWindows && IsStoreAlias(resolvedPath))
            return "윈도우가 기본으로 심어 두는 앱 실행 별칭 스텁입니다(내용이 없는 파일).\n"
                   + "    vman 경로가 PATH 앞에 있으면 여기까지 오지 않습니다.\n"
                   + $"    vman setup --force 로 순서를 되돌린 뒤:  {ReloadHint()}\n"
                   + "    그래도 거슬리면 설정 → 앱 → 고급 앱 설정 → 앱 실행 별칭 에서 "
                   + "python.exe / python3.exe 를 끄세요.";

        if (Platform.IsWsl && resolvedPath.StartsWith("/mnt/", StringComparison.Ordinal))
            return "WSL 이 윈도우 PATH 를 물려받아 윈도우쪽 실행 파일이 먼저 잡힙니다. "
                   + "WSL 안에서는 리눅스 런타임을 쓰는 것이 맞습니다.\n"
                   + $"    {ReloadHint()} 로 이 창을 다시 읽거나, "
                   + "/etc/wsl.conf 에 [interop] appendWindowsPath=false 를 넣으세요.";

        return $"vman setup --force 로 순서를 되돌린 뒤:  {ReloadHint()}";
    }

    /// <summary>윈도우: 스토어 앱 실행 별칭이 깔려 있는지.</summary>
    [SupportedOSPlatform("windows")]
    private static void CheckStoreAliases(List<DoctorFinding> findings)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps");
        if (!Directory.Exists(dir)) return;

        var aliases = new[] { "python.exe", "python3.exe" }
            .Select(n => Path.Combine(dir, n))
            .Where(IsStoreAlias)
            .ToList();

        if (aliases.Count == 0) return;

        // PATH 상에서 WindowsApps 가 vman 보다 앞에 있으면 실제로 가릴 수 있다.
        var effective = EnvStore.EffectivePathEntries();
        int aliasIndex = effective.FindIndex(p => SamePath(p, dir));
        int vmanIndex = effective.FindIndex(p => SamePath(p, Layout.CurrentLink(ToolDef.Python)));
        bool shadows = aliasIndex >= 0 && (vmanIndex < 0 || aliasIndex < vmanIndex);

        // 이 스텁은 윈도우에 기본으로 있는 파일이다. vman 이 앞서 있으면 아무 문제가 없으므로
        // 굳이 경고하지 않는다. 실제로 앞을 막고 있을 때만 보고한다.
        if (!shadows) return;

        findings.Add(new DoctorFinding(DoctorLevel.Error,
            "앱 실행 별칭이 vman 보다 PATH 앞에 있습니다",
            string.Join("\n", aliases),
            "python 을 쳤을 때 버전 대신 'Python' 한 줄만 나오고 스토어 설치 안내가 뜨는 원인입니다.\n"
            + $"    vman setup --force 로 되돌린 뒤:  {ReloadHint()}\n"
            + "    설정 → 앱 → 고급 앱 설정 → 앱 실행 별칭 에서 아예 꺼도 됩니다."));
    }

    /// <summary>0바이트 스텁이면 스토어 앱 실행 별칭이다(진짜 exe 가 아니다).</summary>
    private static bool IsStoreAlias(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists
                   && fi.Length == 0
                   && fi.FullName.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>WSL: 윈도우 PATH 가 딸려 들어왔는지 알려준다.</summary>
    private static void CheckWslInterop(List<DoctorFinding> findings)
    {
        var winEntries = EnvStore.EffectivePathEntries()
            .Where(p => p.StartsWith("/mnt/", StringComparison.Ordinal))
            .ToList();

        if (winEntries.Count == 0) return;

        findings.Add(new DoctorFinding(DoctorLevel.Ok,
            $"윈도우 PATH {winEntries.Count}개 항목이 WSL 로 딸려 들어와 있습니다",
            "vman 경로가 앞에 있으면 문제되지 않습니다. WSL 안의 vman 은 리눅스 런타임만 관리합니다.\n"
            + "    윈도우쪽 vman 설치본과는 루트가 달라 서로 섞이지 않습니다."));
    }

    // ---------- 헬퍼 ----------

    /// <summary>셸에 심어 둔 vman 함수를 통해 불렸는지. 함수가 VMAN_SHELL 을 세팅한다.</summary>
    private static bool ViaWrapper()
        => Environment.GetEnvironmentVariable("VMAN_SHELL") is { Length: > 0 };

    /// <summary>
    /// "지금 이 창"을 고치는 가장 짧은 방법. 사용자의 실제 상황에 맞는 것만 알려 준다.
    ///
    /// 셸 함수가 있으면 `vman reload` 한 마디로 끝난다. 아직 없으면(첫 설치 전, 또는 cmd)
    /// 그 명령은 안내만 하고 끝나므로, 곧바로 먹는 한 줄을 대신 알려 준다.
    /// </summary>
    private static string ReloadHint()
        => ViaWrapper()
            ? "vman reload"
            : ShellCode.HowToApply(ShellCode.Detect()).Replace("vman env", "vman env --reload");

    /// <summary>PATH 를 앞에서부터 훑어 명령이 처음 걸리는 실제 파일 경로.</summary>
    public static string? ResolveCommand(IEnumerable<string> pathEntries, string command)
    {
        foreach (string dir in pathEntries)
        {
            string candidate;
            try { candidate = Path.Combine(dir, command); }
            catch (ArgumentException) { continue; }   // PATH 에 섞인 깨진 항목

            if (!File.Exists(candidate)) continue;
            if (Platform.IsUnix && !IsExecutable(candidate)) continue;
            return candidate;
        }
        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (Platform.IsWindows) return true;
        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception) { return true; }
    }

    private static bool SamePath(string a, string b)
    {
        var comparison = Platform.IsWindows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), comparison);
    }
}
