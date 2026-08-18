using System.Drawing.Drawing2D;

namespace VMan.Tray.Theming;

/// <summary>
/// ToolStrip 의 기본 그리기를 전부 대체한다.
/// 회색 이미지 여백, 각진 선택 사각형, 두꺼운 테두리 같은 옛날 윈도우 흔적을 지우고
/// 테마가 정한 색/여백/둥근 모서리로 다시 그린다.
/// </summary>
internal sealed class ThemedRenderer : ToolStripRenderer
{
    private readonly Func<Theme> _theme;
    private readonly Func<bool> _dark;

    public ThemedRenderer(Func<Theme> theme, Func<bool> dark)
    {
        _theme = theme;
        _dark = dark;
    }

    private Theme T => _theme();
    private Palette P => T.For(_dark());

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(Point.Empty, e.ToolStrip.Size);
        using var brush = new SolidBrush(P.Background);

        // DWM 이 창을 잘라주는 경우엔 우리가 또 둥글게 칠하면 모서리에 빈틈이 생긴다.
        if (T.UseDwmCorners || T.CornerRadius <= 0)
        {
            g.FillRectangle(brush, rect);
            return;
        }

        using var path = Native.RoundedRect(
            new Rectangle(0, 0, rect.Width - 1, rect.Height - 1), T.CornerRadius);
        g.FillPath(brush, path);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (T.UseDwmCorners || T.CornerRadius <= 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Native.RoundedRect(
            new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), T.CornerRadius);
        using var pen = new Pen(P.Border);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is VmanMenuItem { IsHeader: true }) return;
        if (!e.Item.Selected || !e.Item.Enabled) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int inset = T.HighlightInset;
        var r = new Rectangle(
            inset, 1,
            e.Item.Width - inset * 2, e.Item.Height - 2);
        if (r.Width <= 0 || r.Height <= 0) return;

        using var brush = new SolidBrush(P.Highlight);
        using var path = Native.RoundedRect(r, T.HighlightRadius);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is not VmanMenuItem item)
        {
            base.OnRenderItemText(e);
            return;
        }

        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool selected = item.Selected && item.Enabled && !item.IsHeader;
        Color primary = item.IsHeader ? P.Muted
                      : !item.Enabled ? P.Muted
                      : selected ? P.HighlightText
                      : P.Foreground;

        int left = T.RowPaddingX;
        int right = item.Width - T.RowPaddingX;
        if (item.HasDropDownItems) right -= T.ArrowSpace;

        // 체크 표시
        if (item.NeedsCheckColumn)
        {
            if (item.Marked)
                DrawCheck(g, new Rectangle(left, 0, item.CheckColumnWidth, item.Height),
                    selected ? P.HighlightText : P.Accent);
            left += item.CheckColumnWidth;
        }

        // 오른쪽 보조 텍스트를 먼저 그려 남는 폭을 확정한다
        if (!string.IsNullOrEmpty(item.Secondary))
        {
            var sz = TextRenderer.MeasureText(item.Secondary, item.SecondaryFont);
            var sr = new Rectangle(right - sz.Width, 0, sz.Width, item.Height);
            TextRenderer.DrawText(g, item.Secondary, item.SecondaryFont, sr,
                selected ? P.HighlightText : P.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            right -= sz.Width + T.GapBeforeSecondary;
        }

        var textRect = new Rectangle(left, 0, Math.Max(0, right - left), item.Height);
        TextRenderer.DrawText(g, item.Text, item.PrimaryFont, textRect, primary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item is not { } item) return;

        int y = item.Height / 2;
        using var pen = new Pen(P.Separator);
        e.Graphics.DrawLine(pen, T.SeparatorInset, y, item.Width - T.SeparatorInset, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (e.Item is not { } item) return;

        bool selected = item is { Selected: true, Enabled: true };
        Color color = selected ? P.HighlightText : P.Muted;

        // 화살표는 ArrowRectangle 을 무시하고 항목 오른쪽 여백 기준으로 그린다.
        // 그래야 GetPreferredSize 가 잡아둔 예약폭과 정확히 맞아떨어진다.
        int cx = item.Width - T.RowPaddingX - 5;
        int cy = item.Height / 2;
        int h = T.RowHeight >= 40 ? 5 : 4;

        // 서브메뉴가 왼쪽으로 펴지면 화살표도 왼쪽을 가리켜야 한다
        bool left = item is ToolStripDropDownItem { DropDownDirection: ToolStripDropDownDirection.Left };
        float tip = left ? -0.6f : 0.4f;
        float tail = left ? 0.4f : -0.6f;

        using var pen = new Pen(color, T.RowHeight >= 40 ? 1.8f : 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLines(pen, new[]
        {
            new PointF(cx + tail * h, cy - h),
            new PointF(cx + tip * h, cy),
            new PointF(cx + tail * h, cy + h)
        });
    }

    // 기본 체크/이미지 여백은 완전히 없앤다
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    private static void DrawCheck(Graphics g, Rectangle area, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = area.X + 2;
        float cy = area.Y + area.Height / 2f;
        float s = Math.Min(area.Height, 16) * 0.42f;

        using var pen = new Pen(color, 1.9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLines(pen, new[]
        {
            new PointF(cx, cy),
            new PointF(cx + s * 0.75f, cy + s * 0.75f),
            new PointF(cx + s * 2.0f, cy - s * 0.9f)
        });
    }
}
