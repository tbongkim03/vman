namespace VMan.Tray.Theming;

/// <summary>
/// 드롭다운 하나하나에 테마를 입힌다.
/// 서브메뉴는 각각 별개의 창이라 만들어질 때마다 똑같이 손봐줘야 한다.
/// </summary>
internal sealed class MenuHost
{
    private readonly Func<Theme> _theme;
    private readonly Func<bool> _dark;
    private readonly ThemedRenderer _renderer;

    public MenuHost(Func<Theme> theme, Func<bool> dark)
    {
        _theme = theme;
        _dark = dark;
        _renderer = new ThemedRenderer(theme, dark);
    }

    public ThemedRenderer Renderer => _renderer;

    /// <summary>
    /// 트레이가 화면 오른쪽 끝에 있으면 서브메뉴를 왼쪽으로 편다.
    /// 기본값(Default)에 맡기면 레벨마다 공간에 따라 방향이 뒤집혀
    /// 2차는 왼쪽, 3차는 가운데, 4차는 오른쪽으로 튀며 서로 겹친다.
    /// </summary>
    public bool PreferLeft { get; set; }

    public ContextMenuStrip CreateRoot()
    {
        var menu = new VmanContextMenu(_theme);
        Style(menu);
        return menu;
    }

    /// <summary>드롭다운(루트 메뉴 또는 서브메뉴)에 렌더러/여백/둥근 모서리를 적용한다.</summary>
    public void Style(ToolStripDropDown dd)
    {
        var t = _theme();

        dd.Renderer = _renderer;
        dd.BackColor = t.For(_dark()).Background;

        // 창을 직접 잘라낼 때는 사각 그림자가 모서리 밖으로 삐져나와 각져 보인다.
        dd.DropShadowEnabled = t.UseDwmCorners;

        if (dd is ToolStripDropDownMenu m)
        {
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
        }

        // 크기가 확정된 뒤에 모양을 잡아야 한다
        dd.Opened -= OnOpened;
        dd.Opened += OnOpened;
        dd.Resize -= OnResize;
        dd.Resize += OnResize;
    }

    private void OnOpened(object? sender, EventArgs e) => ApplyShape(sender as ToolStripDropDown);
    private void OnResize(object? sender, EventArgs e) => ApplyShape(sender as ToolStripDropDown);

    private void ApplyShape(ToolStripDropDown? dd)
    {
        if (dd is null || !dd.IsHandleCreated || dd.Width <= 0 || dd.Height <= 0) return;

        var t = _theme();
        Native.SetDarkMode(dd.Handle, _dark());

        if (t.UseDwmCorners)
        {
            // DWM 이 잘라주면 안티에일리어싱과 그림자가 훨씬 깔끔하다 (반지름은 8px 고정).
            dd.Region = null;
            Native.SetCornerPreference(dd.Handle, round: true);
            return;
        }

        // 큰 반지름은 DWM 이 못 하므로 창을 직접 잘라낸다.
        Native.SetCornerPreference(dd.Handle, round: false);

        if (t.CornerRadius <= 0)
        {
            dd.Region = null;
            return;
        }

        using var path = Native.RoundedRect(new Rectangle(0, 0, dd.Width, dd.Height), t.CornerRadius);
        dd.Region = new Region(path);
    }

    /// <summary>
    /// 항목에 서브메뉴를 달면서 그 서브메뉴에도 테마를 입힌다.
    /// 자동 생성되는 기본 드롭다운은 크기 계산이 어긋나므로 우리 것으로 갈아끼운다.
    /// </summary>
    public void AttachSubmenu(ToolStripMenuItem parent, params ToolStripItem[] children)
    {
        var dd = new VmanDropDown(_theme);
        Style(dd);
        dd.Items.AddRange(children);
        parent.DropDown = dd;
        parent.DropDownDirection = PreferLeft
            ? ToolStripDropDownDirection.Left
            : ToolStripDropDownDirection.Right;
    }
}
