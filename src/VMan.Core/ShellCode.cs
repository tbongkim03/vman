using System.Text;

namespace VMan.Core;

public enum ShellKind
{
    /// <summary>bash · zsh · dash 등 POSIX 계열.</summary>
    PosixShell,
    Fish,
    PowerShell,
    Cmd
}

/// <summary>
/// "지금 이 창에 바로 적용"을 가능하게 하는 조각.
///
/// 프로세스는 부모 셸의 환경을 바꿀 수 없다. 예외는 없다.
/// 그래서 버전 관리자들이 쓰는 방법은 하나뿐이다 —
/// <b>셸이 스스로 실행할 코드를 문자열로 뱉고, 셸이 그것을 eval 한다.</b>
///
/// 여기서는 최종 PATH 를 셸 문법이 아니라 C# 에서 계산한다.
/// 셸 쪽에는 대입문 한 줄만 나가므로 sh · fish · PowerShell · cmd 를
/// 같은 로직으로 지원할 수 있고, 셸 스크립트에 논리가 흩어지지 않는다.
/// </summary>
public static class ShellCode
{
    /// <summary>어떤 셸에서 불렸는지 추정한다. 확실하지 않으므로 --shell 로 덮어쓸 수 있다.</summary>
    public static ShellKind Detect()
    {
        // 1) 셸에 심어 둔 vman 함수가 알려준 값이 가장 정확하다.
        if (Parse(Environment.GetEnvironmentVariable("VMAN_SHELL")) is { } declared)
            return declared;

        if (Platform.IsWindows)
        {
            // PowerShell 은 이 변수를 만들어 둔다. cmd 는 만들지 않는다.
            // (PowerShell 에서 띄운 cmd 는 물려받으므로 완벽하지는 않다 → --shell 로 지정)
            return Environment.GetEnvironmentVariable("PSModulePath") is { Length: > 0 }
                ? ShellKind.PowerShell
                : ShellKind.Cmd;
        }

        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
        return Parse(Path.GetFileName(shell)) ?? ShellKind.PosixShell;
    }

    public static ShellKind? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "posix" or "sh" or "bash" or "zsh" or "dash" or "ksh" => ShellKind.PosixShell,
        "fish" => ShellKind.Fish,
        "powershell" or "pwsh" or "powershell.exe" or "pwsh.exe" or "ps" => ShellKind.PowerShell,
        "cmd" or "cmd.exe" or "bat" => ShellKind.Cmd,
        _ => null
    };

    /// <summary>vman 경로를 앞에 붙인 환경을 이 셸에 적용하는 코드.</summary>
    public static string Apply(ShellKind kind) => Emit(kind, revert: false, fromSystem: false);

    /// <summary>vman 경로를 걷어낸 환경으로 되돌리는 코드.</summary>
    public static string Revert(ShellKind kind) => Emit(kind, revert: true, fromSystem: false);

    /// <summary>
    /// 이 창의 환경을 <b>지금 새 터미널을 열면 갖게 될 것</b>으로 통째로 갈아끼우는 코드.
    /// `source ~/.zshrc` 에 해당하는 동작이다.
    ///
    /// Apply 와 다른 점은 출발점이다. Apply 는 이 창의 현재 PATH 에 vman 경로만 얹지만,
    /// Reload 는 시스템이 들고 있는 값에서 다시 시작한다. 그래서 vman 이 아닌 다른
    /// 설치 프로그램이 PATH 를 바꿔 놓은 것까지 따라온다.
    /// 윈도우에서는 레지스트리(HKLM + HKCU), 리눅스에서는 그런 중앙 저장소가 없으므로
    /// 현재 PATH 가 곧 출발점이고 결과적으로 Apply 와 같아진다.
    /// </summary>
    public static string Reload(ShellKind kind) => Emit(kind, revert: false, fromSystem: true);

    private static string Emit(ShellKind kind, bool revert, bool fromSystem)
    {
        var wanted = Layout.AllPathEntries().ToList();
        var session = fromSystem
            ? EnvStore.EffectivePathEntries()
            : EnvStore.SessionPathEntries();

        // 중복 없이: vman 항목을 일단 전부 걷어낸 뒤, 되돌리기가 아니면 맨 앞에 다시 붙인다.
        var kept = session.Where(p => !wanted.Any(w => SamePath(w, p)));
        var final = revert ? kept.ToList() : wanted.Concat(kept).ToList();

        string path = string.Join(Platform.PathSeparator, final);

        // JAVA_HOME 은 버전이 아니라 링크 자신을 가리키므로 값이 변하지 않는다.
        // 링크가 없는(= 지정 해제) 상태에서는 깨진 경로를 내보내지 않는다.
        string? javaHome = null;
        var java = ToolDef.Java;
        if (!revert && java.HomeEnvVar is not null && Directory.Exists(Layout.CurrentLink(java)))
            javaHome = Layout.CurrentLink(java);

        var sb = new StringBuilder();
        switch (kind)
        {
            case ShellKind.PosixShell:
                sb.AppendLine($"export PATH={PosixQuote(path)}");
                if (javaHome is not null) sb.AppendLine($"export JAVA_HOME={PosixQuote(javaHome)}");
                else if (revert) sb.AppendLine($"unset {java.HomeEnvVar}");
                break;

            case ShellKind.Fish:
                sb.AppendLine($"set -gx PATH {FishQuote(path)}");
                if (javaHome is not null) sb.AppendLine($"set -gx JAVA_HOME {FishQuote(javaHome)}");
                else if (revert) sb.AppendLine($"set -e {java.HomeEnvVar}");
                break;

            case ShellKind.PowerShell:
                sb.AppendLine($"$env:PATH = {PowerShellQuote(path)}");
                if (javaHome is not null) sb.AppendLine($"$env:JAVA_HOME = {PowerShellQuote(javaHome)}");
                else if (revert) sb.AppendLine($"Remove-Item Env:\\{java.HomeEnvVar} -ErrorAction SilentlyContinue");
                break;

            case ShellKind.Cmd:
                sb.AppendLine($"set \"PATH={path}\"");
                if (javaHome is not null) sb.AppendLine($"set \"JAVA_HOME={javaHome}\"");
                else if (revert) sb.AppendLine($"set \"{java.HomeEnvVar}=\"");
                break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 가상환경을 이 셸에 활성화하는 코드.
    ///
    /// venv 가 딸려 주는 activate 스크립트를 부르지 않고 직접 만든다. 이유가 둘이다.
    ///   1) 셸마다 activate / activate.fish / Activate.ps1 로 파일이 갈리는데,
    ///      여기서는 어차피 PATH 대입문 한 줄이면 끝난다.
    ///   2) vman 경로와의 순서를 우리가 정할 수 있다. 가상환경이 vman 보다 앞이어야
    ///      pip 과 python 이 가상환경 것으로 잡힌다.
    /// </summary>
    public static string Activate(ShellKind kind, Venv venv)
    {
        var session = EnvStore.SessionPathEntries();

        // 이전에 활성화해 둔 가상환경이 있으면 그 bin 을 먼저 걷어낸다.
        // 그래야 가상환경을 옮겨 다녀도 PATH 가 쌓이지 않는다.
        var stale = PreviousVenvBinDirs();
        var kept = session.Where(p => !stale.Any(s => SamePath(s, p)));

        string path = string.Join(Platform.PathSeparator, new[] { venv.BinDir }.Concat(kept));

        var sb = new StringBuilder();
        AppendAssign(sb, kind, "PATH", path);
        AppendAssign(sb, kind, "VIRTUAL_ENV", venv.Path);
        // 가상환경 안에서는 PYTHONHOME 이 있으면 오히려 방해가 된다.
        AppendUnset(sb, kind, "PYTHONHOME");
        return sb.ToString();
    }

    /// <summary>가상환경을 이 셸에서 걷어내는 코드.</summary>
    public static string Deactivate(ShellKind kind)
    {
        var stale = PreviousVenvBinDirs();
        var kept = EnvStore.SessionPathEntries().Where(p => !stale.Any(s => SamePath(s, p)));

        var sb = new StringBuilder();
        AppendAssign(sb, kind, "PATH", string.Join(Platform.PathSeparator, kept));
        AppendUnset(sb, kind, "VIRTUAL_ENV");
        return sb.ToString();
    }

    /// <summary>지금 활성화되어 있는 가상환경의 bin 경로들 (PATH 에서 뺄 대상).</summary>
    private static List<string> PreviousVenvBinDirs()
    {
        string? active = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        if (string.IsNullOrWhiteSpace(active)) return new List<string>();

        var venv = new Venv(active, Path.GetFileName(active.TrimEnd('\\', '/')));
        return new List<string> { venv.BinDir };
    }

    private static void AppendAssign(StringBuilder sb, ShellKind kind, string name, string value)
    {
        switch (kind)
        {
            case ShellKind.PosixShell:
                sb.AppendLine($"export {name}={PosixQuote(value)}"); break;
            case ShellKind.Fish:
                sb.AppendLine($"set -gx {name} {FishQuote(value)}"); break;
            case ShellKind.PowerShell:
                sb.AppendLine($"$env:{name} = {PowerShellQuote(value)}"); break;
            case ShellKind.Cmd:
                sb.AppendLine($"set \"{name}={value}\""); break;
        }
    }

    private static void AppendUnset(StringBuilder sb, ShellKind kind, string name)
    {
        switch (kind)
        {
            case ShellKind.PosixShell: sb.AppendLine($"unset {name}"); break;
            case ShellKind.Fish: sb.AppendLine($"set -e {name}"); break;
            case ShellKind.PowerShell:
                sb.AppendLine($"Remove-Item Env:\\{name} -ErrorAction SilentlyContinue"); break;
            case ShellKind.Cmd: sb.AppendLine($"set \"{name}=\""); break;
        }
    }

    /// <summary>셸에 심어 둔 vman 함수가 없을 때 사람이 직접 칠 한 줄.</summary>
    public static string HowToApply(ShellKind kind) => kind switch
    {
        ShellKind.PosixShell => "eval \"$(vman env)\"",
        ShellKind.Fish => "vman env --shell fish | source",
        ShellKind.PowerShell => "vman env | Out-String | Invoke-Expression",
        _ => "for /f \"delims=\" %i in ('vman env --shell cmd') do @%i"
    };

    // ---------- 인용 ----------

    /// <summary>작은따옴표로 감싼다. 안의 ' 는 '\'' 로 끊어 붙인다.</summary>
    private static string PosixQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>fish 는 작은따옴표 안에서 \' 와 \\ 만 이스케이프한다.</summary>
    private static string FishQuote(string s) =>
        "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>PowerShell 의 작은따옴표 문자열. 안의 ' 는 '' 로 겹쳐 쓴다.</summary>
    private static string PowerShellQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private static bool SamePath(string a, string b)
    {
        var cmp = Platform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), cmp);
    }
}
