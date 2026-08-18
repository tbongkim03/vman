namespace VMan.Tray.Theming;

/// <summary>
/// ToolStripDropDownMenu 는 자기 크기를 이미지/체크 여백을 전제로 계산하고
/// Padding 도 {Left=8, Right=1} 로 되돌려 놓는다. 그 결과 창이 항목보다 좁아져
/// 오른쪽 텍스트가 잘린다. 크기 계산과 여백을 통째로 가져온다.
/// </summary>
internal static class DropDownSizing
{
    public static Size Compute(ToolStripDropDownMenu dd)
    {
        int width = 0, height = 0;

        foreach (ToolStripItem item in dd.Items)
        {
            if (!item.Available) continue;

            if (item is VmanMenuItem v)
            {
                var s = v.GetPreferredSize(Size.Empty);
                width = Math.Max(width, s.Width);
                height += s.Height;
            }
            else
            {
                height += item.GetPreferredSize(Size.Empty).Height;
            }
        }

        return new Size(width + dd.Padding.Horizontal, height + dd.Padding.Vertical);
    }
}

/// <summary>서브메뉴용 드롭다운.</summary>
internal sealed class VmanDropDown : ToolStripDropDownMenu
{
    private readonly Func<Theme>? _theme;

    public VmanDropDown(Func<Theme> theme)
    {
        _theme = theme;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        AutoSize = true;
    }

    private Theme T => _theme?.Invoke() ?? Theme.Apple;

    protected override Padding DefaultPadding => new(0, T.MenuPadding, 0, T.MenuPadding);

    public override Size GetPreferredSize(Size proposedSize) => DropDownSizing.Compute(this);
}

/// <summary>NotifyIcon 에 붙일 루트 메뉴. ContextMenuStrip 이어야 해서 따로 둔다.</summary>
internal sealed class VmanContextMenu : ContextMenuStrip
{
    private readonly Func<Theme>? _theme;

    public VmanContextMenu(Func<Theme> theme)
    {
        _theme = theme;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        AutoSize = true;
    }

    private Theme T => _theme?.Invoke() ?? Theme.Apple;

    protected override Padding DefaultPadding => new(0, T.MenuPadding, 0, T.MenuPadding);

    public override Size GetPreferredSize(Size proposedSize) => DropDownSizing.Compute(this);
}
