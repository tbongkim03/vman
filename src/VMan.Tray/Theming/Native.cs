using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VMan.Tray.Theming;

/// <summary>둥근 모서리, 다크 모드 감지 등 윈도우 쪽 잡일.</summary>
internal static class Native
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// 시스템이 어두운 앱 모드인지.
    /// 레지스트리를 직접 본다 — 사용자가 설정을 바꾸면 메뉴를 열 때마다 반영된다.
    /// </summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>DWM 에게 이 창의 모서리 처리를 맡길지 알려준다.</summary>
    public static void SetCornerPreference(IntPtr hwnd, bool round)
    {
        if (hwnd == IntPtr.Zero) return;
        int pref = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch (Exception) { /* 구버전 윈도우면 무시 */ }
    }

    /// <summary>창 테두리/그림자를 어두운 쪽으로.</summary>
    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        int on = dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
        catch (Exception) { /* 무시 */ }
    }

    /// <summary>모서리가 둥근 사각형 경로. 큰 반지름은 DWM 이 못 하므로 직접 그린다.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>설치되어 있는 첫 번째 글꼴을 고른다.</summary>
    public static string PickFont(params string[] candidates)
    {
        var installed = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string c in candidates)
            if (installed.Contains(c)) return c;
        return "Segoe UI";
    }
}
