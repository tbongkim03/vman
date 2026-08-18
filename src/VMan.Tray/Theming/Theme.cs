namespace VMan.Tray.Theming;

/// <summary>한 가지 모드(밝게/어둡게)의 색 묶음.</summary>
internal sealed record Palette(
    Color Background,
    Color Foreground,
    Color Muted,
    Color Highlight,
    Color HighlightText,
    Color Separator,
    Color Border,
    Color Accent);

/// <summary>메뉴 전체의 생김새. Apple / One UI 두 가지를 준비해 두고 런타임에 바꾼다.</summary>
internal sealed record Theme(
    string Id,
    string Name,
    int CornerRadius,
    int RowHeight,
    int RowPaddingX,
    int HighlightInset,
    int HighlightRadius,
    int MenuPadding,
    int SeparatorInset,
    float FontSize,
    float SecondaryFontSize,
    bool BoldPrimary,
    int ArrowSpace,
    int MinWidth,
    int CompactMinWidth,
    int GapBeforeSecondary,
    bool UseDwmCorners,
    Palette Light,
    Palette Dark)
{
    public Palette For(bool dark) => dark ? Dark : Light;

    /// <summary>macOS 메뉴바 드롭다운. 촘촘하고 조용한 회색조에 파란 선택 바.</summary>
    public static readonly Theme Apple = new(
        Id: "apple",
        Name: "Apple",
        CornerRadius: 8,
        RowHeight: 26,
        RowPaddingX: 13,
        HighlightInset: 5,
        HighlightRadius: 5,
        MenuPadding: 5,
        SeparatorInset: 12,
        FontSize: 9f,
        SecondaryFontSize: 9f,
        BoldPrimary: false,
        ArrowSpace: 20,
        MinWidth: 208,
        CompactMinWidth: 116,
        GapBeforeSecondary: 22,
        UseDwmCorners: true,
        Light: new Palette(
            Background: Color.FromArgb(246, 246, 248),
            Foreground: Color.FromArgb(29, 29, 31),
            Muted: Color.FromArgb(142, 142, 147),
            Highlight: Color.FromArgb(10, 132, 255),
            HighlightText: Color.White,
            Separator: Color.FromArgb(214, 214, 218),
            Border: Color.FromArgb(198, 198, 203),
            Accent: Color.FromArgb(10, 132, 255)),
        Dark: new Palette(
            Background: Color.FromArgb(44, 44, 46),
            Foreground: Color.FromArgb(245, 245, 247),
            Muted: Color.FromArgb(152, 152, 157),
            Highlight: Color.FromArgb(10, 132, 255),
            HighlightText: Color.White,
            Separator: Color.FromArgb(64, 64, 67),
            Border: Color.FromArgb(74, 74, 78),
            Accent: Color.FromArgb(10, 132, 255)));

    /// <summary>One UI 8. 크게 둥근 모서리, 넉넉한 여백, 알약 모양 선택 표시.</summary>
    public static readonly Theme OneUi = new(
        Id: "oneui",
        Name: "One UI 8",
        CornerRadius: 20,
        RowHeight: 40,
        RowPaddingX: 20,
        HighlightInset: 6,
        HighlightRadius: 14,
        MenuPadding: 10,
        SeparatorInset: 20,
        FontSize: 10f,
        SecondaryFontSize: 9f,
        BoldPrimary: true,
        ArrowSpace: 26,
        MinWidth: 268,
        CompactMinWidth: 150,
        GapBeforeSecondary: 26,
        UseDwmCorners: false,
        Light: new Palette(
            Background: Color.FromArgb(252, 252, 252),
            Foreground: Color.FromArgb(27, 27, 27),
            Muted: Color.FromArgb(138, 138, 142),
            Highlight: Color.FromArgb(231, 240, 255),
            HighlightText: Color.FromArgb(11, 87, 208),
            Separator: Color.FromArgb(237, 237, 237),
            Border: Color.FromArgb(226, 226, 228),
            Accent: Color.FromArgb(11, 87, 208)),
        Dark: new Palette(
            Background: Color.FromArgb(27, 27, 29),
            Foreground: Color.FromArgb(241, 241, 241),
            Muted: Color.FromArgb(154, 154, 158),
            Highlight: Color.FromArgb(42, 59, 87),
            HighlightText: Color.FromArgb(168, 199, 250),
            Separator: Color.FromArgb(46, 46, 48),
            Border: Color.FromArgb(56, 56, 59),
            Accent: Color.FromArgb(168, 199, 250)));

    public static readonly IReadOnlyList<Theme> All = new[] { Apple, OneUi };

    public static Theme ById(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Apple;
}
