using System.Runtime.Versioning;

namespace VMan.Core;

/// <summary>
/// 실행 중인 OS를 한 곳에서 판별한다.
/// vman 은 윈도우(레지스트리 + 정션)와 리눅스/WSL2(rc 파일 + 심볼릭 링크) 두 벌의
/// 구현을 갖고 있고, 어느 쪽을 쓸지는 전부 여기를 통해 결정한다.
/// </summary>
public static class Platform
{
    /// <remarks>
    /// SupportedOSPlatformGuard 를 붙여야 분석기가 "if (Platform.IsWindows)" 안쪽을
    /// 윈도우 전용 코드로 인정한다. 없으면 레지스트리 호출마다 CA1416 경고가 뜬다.
    /// </remarks>
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>리눅스 · macOS 등 유닉스 계열.</summary>
    [UnsupportedOSPlatformGuard("windows")]
    public static bool IsUnix => !IsWindows;

    /// <summary>
    /// WSL2 안에서 도는 리눅스인지. 진단 메시지를 다르게 내기 위해 쓴다.
    /// (WSL 은 커널 릴리스 문자열에 microsoft 가 들어간다.)
    /// </summary>
    public static bool IsWsl { get; } = DetectWsl();

    private static bool DetectWsl()
    {
        if (IsWindows) return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"))) return true;
        try
        {
            const string osrelease = "/proc/sys/kernel/osrelease";
            return File.Exists(osrelease)
                   && File.ReadAllText(osrelease).Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>실행 파일 확장자. 윈도우는 .exe, 유닉스는 빈 문자열.</summary>
    public static string ExeSuffix => IsWindows ? ".exe" : "";

    /// <summary>PATH 를 나누는 문자. 윈도우 ';' 유닉스 ':'.</summary>
    public static char PathSeparator => IsWindows ? ';' : ':';

    /// <summary>정의에 '/' 로 적힌 상대 경로를 현재 OS 구분자로 바꾼다.</summary>
    public static string NormalizeRelative(string relative)
        => relative.Replace('/', Path.DirectorySeparatorChar);
}
