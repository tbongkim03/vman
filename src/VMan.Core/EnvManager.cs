using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VMan.Core;

/// <summary>
/// HKCU\Environment 를 직접 다룬다.
/// Environment.SetEnvironmentVariable(..., User) 를 쓰지 않는 이유:
///   1) REG_EXPAND_SZ 를 REG_SZ 로 바꿔버려 %USERPROFILE% 같은 변수가 굳는다
///   2) 구버전에서 1024자 절단 버그가 있다
/// 쓰기 전에 항상 백업을 남긴다.
/// </summary>
public static class EnvManager
{
    private const string SubKey = "Environment";
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

    /// <summary>주어진 경로들을 사용자 PATH 맨 앞에 넣는다(중복 제거). 이미 다 있으면 false.</summary>
    public static bool PrependToUserPath(IEnumerable<string> entries)
    {
        var (current, kind) = ReadRaw("Path");
        if (kind != RegistryValueKind.ExpandString && kind != RegistryValueKind.String)
            kind = RegistryValueKind.ExpandString;

        var existing = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .ToList();

        var wanted = entries.Select(e => e.TrimEnd('\\')).ToList();
        bool allPresent = wanted.All(w =>
            existing.Any(e => string.Equals(e.TrimEnd('\\'), w, StringComparison.OrdinalIgnoreCase)));
        if (allPresent) return false;

        BackupPath();

        // vman 경로는 전부 제거 후 맨 앞에 다시 붙인다 (순서 보장)
        var kept = existing.Where(e =>
            !wanted.Any(w => string.Equals(e.TrimEnd('\\'), w, StringComparison.OrdinalIgnoreCase)));

        string merged = string.Join(";", wanted.Concat(kept));
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
