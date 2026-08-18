namespace VMan.Tray.Theming;

/// <summary>
/// 기본 ToolStripMenuItem 은 왼쪽에 회색 이미지 여백을 그리고 텍스트를 한 덩어리로만
/// 다룬다. 여기서는 그 여백을 없애고 "왼쪽 이름 + 오른쪽 보조 텍스트" 레이아웃을
/// 직접 계산한다. 실제 그리기는 ThemedRenderer 가 한다.
/// </summary>
internal sealed class VmanMenuItem : ToolStripMenuItem
{
    /// <summary>오른쪽에 옅게 붙는 값 (현재 버전, LTS 꼬리표 등).</summary>
    public string? Secondary { get; set; }

    /// <summary>왼쪽에 체크 표시를 그릴지.</summary>
    public bool Marked { get; set; }

    /// <summary>선택 불가한 구역 제목("설치됨" 같은)으로 그릴지.</summary>
    public bool IsHeader { get; set; }

    /// <summary>버전 목록처럼 내용이 짧은 깊은 메뉴는 좁게 잡는다.</summary>
    public bool Compact { get; set; }

    public Theme Theme { get; set; } = Theme.Apple;

    public VmanMenuItem(string text) : base(text) { }

    public Font PrimaryFont => Fonts.Get(
        IsHeader ? Theme.SecondaryFontSize : Theme.FontSize,
        IsHeader || Theme.BoldPrimary ? FontStyle.Bold : FontStyle.Regular);

    public Font SecondaryFont => Fonts.Get(Theme.SecondaryFontSize);

    /// <summary>체크 표시가 차지하는 왼쪽 열의 너비.</summary>
    public int CheckColumnWidth => Theme.RowHeight >= 40 ? 30 : 20;

    /// <summary>같은 메뉴 안에 체크된 항목이 있으면 모두 같은 들여쓰기를 쓴다.</summary>
    public bool NeedsCheckColumn
    {
        get
        {
            if (Owner is null) return Marked;
            foreach (ToolStripItem item in Owner.Items)
                if (item is VmanMenuItem { Marked: true }) return true;
            return false;
        }
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        int width = Theme.RowPaddingX * 2 + Measure(Text, PrimaryFont);

        if (NeedsCheckColumn) width += CheckColumnWidth;
        if (!string.IsNullOrEmpty(Secondary))
            width += Theme.GapBeforeSecondary + Measure(Secondary, SecondaryFont);
        if (HasDropDownItems) width += Theme.ArrowSpace;

        int height = IsHeader ? (int)(Theme.RowHeight * 0.78) : Theme.RowHeight;
        int floor = Compact ? Theme.CompactMinWidth : Theme.MinWidth;
        return new Size(Math.Max(width, floor), height);
    }

    private static int Measure(string? text, Font font) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font).Width;
}
