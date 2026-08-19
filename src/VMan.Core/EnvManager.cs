using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VMan.Core;

/// <summary>
/// HKCU\Environment 를 직접 다룬다. 윈도우 전용.
/// Environment.SetEnvironmentVariable(..., User) 를 쓰지 않는 이유:
///   1) REG_EXPAND_SZ 를 REG_SZ 로 바꿔버려 %USERPROFILE% 같은 변수가 굳는다
///   2) 구버전에서 1024자 절단 버그가 있다
/// 쓰기 전에 항상 백업을 남긴다.
///
/// 리눅스/WSL 에서는 <see cref="ShellEnv"/> 가 rc 파일로 같은 역할을 한다.
/// 어느 쪽을 부를지는 <see cref="EnvStore"/> 가 정한다.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EnvManager
{
    private const string SubKey = "Environment";
    private const string MachineSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    /// <summary>원본 타입/문자열을 그대로 읽는다(환경변수 전개 금지).</summary>
    public static (string Value, RegistryValueKind Kind) ReadRaw(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        if (key is null) return ("", RegistryValueKind.ExpandString);

        object? raw = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
        RegistryValueKind kind;
        try { kind = key.GetValueKind(name); }
        catch (Exception) { kind = RegistryValueKind.ExpandString; }

        return (raw?.ToString() ?? "", kind);
    }

    /// <summary>시스템(HKLM) PATH. 진단할 때 실효 PATH를 재구성하려고 읽는다.</summary>
    public static string ReadMachinePath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineSubKey, writable: false);
        return key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
    }

    /// <summary>
    /// 지금 새 터미널을 열면 갖게 될 PATH. 시스템 PATH 뒤에 사용자 PATH가 붙는다.
    /// 이미 떠 있는 프로세스의 PATH(낡았을 수 있다) 대신 이것을 봐야 진단이 맞는다.
    /// </summary>
    public static List<string> EffectivePathEntries()
    {
        string machine = Environment.ExpandEnvironmentVariables(ReadMachinePath());
        string user = Environment.ExpandEnvironmentVariables(ReadRaw("Path").Value);
        return (machine + ";" + user)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Trim('"'))
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static void WriteRaw(string name, string value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(SubKey);
        key.SetValue(name, value, kind);
    }

    /// <summary>PATH 수정 전 원본을 타임스탬프 파일로 남긴다.</summary>
    public static string BackupPath()
    {
        var (value, kind) = ReadRaw("Path");
        Directory.CreateDirectory(Layout.BackupDir);
        string file = Path.Combine(Layout.BackupDir,
            $"user-path-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(file, $"# kind={kind}{Environment.NewLine}{value}");
        return file;
    }

    /// <summary>
    /// 주어진 경로들을 사용자 PATH 맨 앞에 넣는다(중복 제거).
    /// 이미 전부 들어 있으면 아무것도 하지 않고 false 를 돌려준다.
    /// </summary>
    /// <param name="force">
    /// true 면 이미 들어 있어도 무조건 맨 앞으로 다시 끌어올린다.
    /// 스토어 Python 처럼 다른 설치 프로그램이 자기 경로를 PATH 앞에 끼워 넣어
    /// vman 이 뒤로 밀렸을 때 순서를 되찾는 용도.
    /// </param>
    public static bool PrependToUserPath(IEnumerable<string> entries, bool force = false)
    {
        var (current, kind) = ReadRaw("Path");
        if (kind != RegistryValueKind.ExpandString && kind != RegistryValueKind.String)
            kind = RegistryValueKind.ExpandString;

        var existing = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .ToList();

        var wanted = entries.Select(e => e.TrimEnd('\\')).ToList();

        // vman 경로는 전부 제거 후 맨 앞에 다시 붙인다 (순서 보장)
        var kept = existing.Where(e =>
            !wanted.Any(w => string.Equals(e.TrimEnd('\\'), w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        string merged = string.Join(";", wanted.Concat(kept));
        if (!force && string.Equals(merged, current, StringComparison.Ordinal)) return false;

        BackupPath();
        WriteRaw("Path", merged, kind);
        return true;
    }

    /// <summary>사용자 PATH에서 vman 경로들을 제거한다.</summary>
    public static void RemoveFromUserPath(IEnumerable<string> entries)
    {
        var (current, kind) = ReadRaw("Path");
        if (kind != RegistryValueKind.ExpandString && kind != RegistryValueKind.String)
            kind = RegistryValueKind.ExpandString;

        var targets = entries.Select(e => e.TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(e => !targets.Contains(e.TrimEnd('\\')))
                          .ToList();

        BackupPath();
        WriteRaw("Path", string.Join(";", kept), kind);
    }

    public static void SetUserVariable(string name, string value)
        => WriteRaw(name, value, RegistryValueKind.String);

    public static void DeleteUserVariable(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
        if (key?.GetValue(name) is not null) key.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>
    /// 환경변수 변경을 시스템에 알린다.
    /// 탐색기와 "앞으로 새로 뜨는" 프로세스에만 반영된다. 이미 열린 터미널은 갱신되지 않는다.
    /// </summary>
    public static void Broadcast()
    {
        SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, IntPtr.Zero,
            "Environment", SMTO_ABORTIFHUNG, 5000, out _);
    }
}
