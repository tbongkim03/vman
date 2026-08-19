namespace VMan.Core;

/// <summary>setup 이 환경을 어떻게 바꿨는지 알려주는 결과.</summary>
/// <param name="Changed">실제로 뭔가 고쳤는지.</param>
/// <param name="TouchedFiles">리눅스에서 수정한 rc 파일 목록 (윈도우는 비어 있다).</param>
/// <param name="NeedsNewShell">새 셸을 열어야 반영되는지.</param>
public sealed record EnvSetupResult(
    bool Changed,
    IReadOnlyList<string> TouchedFiles,
    bool NeedsNewShell);

/// <summary>
/// "PATH 에 vman 경로를 박아 둔다"는 한 가지 일을 OS별 구현으로 갈라 준다.
///   윈도우      : HKCU\Environment 레지스트리   (<see cref="EnvManager"/>)
///   리눅스/WSL  : ~/.bashrc 등 rc 파일 + env.sh (<see cref="ShellEnv"/>)
/// 위쪽 코드(VersionManager, CLI, 트레이)는 이 파일만 본다.
/// </summary>
public static class EnvStore
{
    /// <summary>PATH 에 vman 경로를 등록한다.</summary>
    /// <param name="force">이미 등록되어 있어도 순서를 맨 앞으로 다시 끌어올린다.</param>
    public static EnvSetupResult Setup(bool force = false)
    {
        if (Platform.IsWindows)
        {
            // 레지스트리 — 새로 뜨는 모든 프로세스(GUI 앱 포함)가 보는 곳.
            bool changed = EnvManager.PrependToUserPath(Layout.AllPathEntries(), force);
            if (changed) EnvManager.Broadcast();

            // PowerShell 프로필 — "지금 이 창"에 반영할 수 있게 해 주는 곳.
            var profiles = PowerShellEnv.Install();

            return new EnvSetupResult(changed || profiles.Count > 0, profiles, NeedsNewShell: true);
        }

        // 리눅스는 Install 이 env.sh 를 항상 다시 쓴다. rc 파일이 이미 걸려 있으면
        // touched 가 비는데, --force 는 "다시 적용했다"는 뜻이므로 변경으로 친다.
        var touched = ShellEnv.Install();
        return new EnvSetupResult(force || touched.Count > 0, touched, NeedsNewShell: true);
    }

    /// <summary>vman 이 건드린 PATH 설정을 되돌린다.</summary>
    public static IReadOnlyList<string> Unsetup()
    {
        if (Platform.IsWindows)
        {
            EnvManager.RemoveFromUserPath(Layout.AllPathEntries());
            foreach (var tool in ToolDef.All)
                if (tool.HomeEnvVar is not null)
                    EnvManager.DeleteUserVariable(tool.HomeEnvVar);
            EnvManager.Broadcast();
            return PowerShellEnv.Uninstall();
        }

        return ShellEnv.Uninstall();
    }

    /// <summary>JAVA_HOME 처럼 도구에 딸린 환경변수를 현재 링크로 맞춘다.</summary>
    public static void SetToolHome(ToolDef tool)
    {
        if (tool.HomeEnvVar is null) return;

        if (Platform.IsWindows)
        {
            EnvManager.SetUserVariable(tool.HomeEnvVar, Layout.CurrentLink(tool));
            return;
        }

        // 리눅스에서는 env.sh 가 링크 존재 여부를 보고 알아서 내보낸다.
        // 값이 링크 경로로 고정이라 버전을 바꿔도 다시 쓸 것이 없다.
    }

    /// <summary>도구 지정을 해제할 때 딸린 환경변수를 치운다.</summary>
    public static void ClearToolHome(ToolDef tool)
    {
        if (tool.HomeEnvVar is null) return;
        if (Platform.IsWindows) EnvManager.DeleteUserVariable(tool.HomeEnvVar);
        // 리눅스는 env.sh 의 [ -d ... ] 검사가 알아서 처리한다.
    }

    /// <summary>vman 블록이 심긴 셸 설정 파일들 (윈도우는 PowerShell 프로필, 리눅스는 rc 파일).</summary>
    public static IReadOnlyList<string> IntegratedShellFiles()
        => Platform.IsWindows ? PowerShellEnv.InstalledProfiles() : ShellEnv.InstalledRcFiles();

    /// <summary>env 파일이 있고 셸 설정이 그것을 읽도록 되어 있는지.</summary>
    public static bool ShellIntegrationInstalled()
        => File.Exists(Platform.IsWindows ? PowerShellEnv.EnvFile : Layout.ShellEnvFile)
           && IntegratedShellFiles().Count > 0;

    /// <summary>지금 이 창을 셸 연동 상태로 만드는 한 줄 (새 터미널을 열지 않을 때).</summary>
    public static string SourceProfileCommand()
        => Platform.IsWindows ? ". $PROFILE" : "source ~/.profile";

    /// <summary>환경변수 변경을 시스템에 알린다(윈도우 전용, 리눅스는 무의미).</summary>
    public static void Broadcast()
    {
        if (Platform.IsWindows) EnvManager.Broadcast();
    }

    /// <summary>
    /// 지금 <b>새 터미널을 열면</b> 갖게 될 PATH 항목들.
    /// 윈도우는 레지스트리(HKLM + HKCU)에서 재구성한다. 현재 프로세스의 PATH 는
    /// 프로세스가 뜰 때 복사된 사본이라 낡았을 수 있어서 진단 근거로 쓸 수 없다.
    /// 리눅스는 rc 파일이 이미 적용된 현재 PATH 가 곧 그 답이다.
    /// </summary>
    public static List<string> EffectivePathEntries()
    {
        if (Platform.IsWindows) return EnvManager.EffectivePathEntries();
        return SessionPathEntries();
    }

    /// <summary>
    /// <b>지금 이 터미널</b>이 실제로 들고 있는 PATH.
    /// 위의 EffectivePathEntries 와 어긋나면 이 창이 설정 변경 전에 열린 창이라는 뜻이다.
    /// </summary>
    public static List<string> SessionPathEntries()
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Platform.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Trim('"'))
            .Where(s => s.Length > 0)
            .ToList();
}
