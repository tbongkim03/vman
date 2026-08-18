using Microsoft.Win32;

namespace VMan.Tray;

/// <summary>
/// 윈도우 11 은 트레이 아이콘의 "숨김/항상 표시" 상태를
/// HKCU\Control Panel\NotifyIconSettings\{해시}\IsPromoted 에 둔다.
/// 문서화된 API 는 없어서 레지스트리를 직접 다룬다.
/// 항목은 아이콘이 한 번이라도 등록된 뒤에 생기므로, 앱이 뜬 다음에 호출해야 한다.
/// </summary>
internal static class TrayPromotion
{
    private const string BaseKey = @"Control Panel\NotifyIconSettings";

    private static IEnumerable<string> OwnSubKeys()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) yield break;

        using var root = Registry.CurrentUser.OpenSubKey(BaseKey);
        if (root is null) yield break;

        foreach (string name in root.GetSubKeyNames())
        {
            using var sub = root.OpenSubKey(name);
            if (sub?.GetValue("ExecutablePath") is string p
                && string.Equals(p, exe, StringComparison.OrdinalIgnoreCase))
                yield return name;
        }
    }

    /// <summary>항상 표시 상태. 항목이 아직 없으면 null.</summary>
    public static bool? IsPromoted()
    {
        using var root = Registry.CurrentUser.OpenSubKey(BaseKey);
        if (root is null) return null;

        bool? result = null;
        foreach (string name in OwnSubKeys())
        {
            using var sub = root.OpenSubKey(name);
            if (sub?.GetValue("IsPromoted") is int v) result = v != 0;
            else result ??= false;
        }
        return result;
    }

    /// <summary>항상 표시를 켜거나 끈다. 바뀐 항목이 하나라도 있으면 true.</summary>
    public static bool SetPromoted(bool on)
    {
        using var root = Registry.CurrentUser.OpenSubKey(BaseKey, writable: true);
        if (root is null) return false;

        bool changed = false;
        foreach (string name in OwnSubKeys())
        {
            using var sub = root.OpenSubKey(name, writable: true);
            if (sub is null) continue;
            sub.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord);
            changed = true;
        }
        return changed;
    }
}
