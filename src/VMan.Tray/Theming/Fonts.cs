using System.Collections.Concurrent;

namespace VMan.Tray.Theming;

/// <summary>글꼴은 메뉴를 그릴 때마다 새로 만들면 낭비이므로 한 번 만들어 재사용한다.</summary>
internal static class Fonts
{
    private static readonly ConcurrentDictionary<(float Size, FontStyle Style), Font> Cache = new();

    private static readonly Lazy<string> Family = new(() =>
        Native.PickFont("Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI"));

    public static Font Get(float size, FontStyle style = FontStyle.Regular) =>
        Cache.GetOrAdd((size, style), k => new Font(Family.Value, k.Size, k.Style));
}
