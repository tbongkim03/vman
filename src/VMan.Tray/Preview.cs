using VMan.Tray.Theming;

namespace VMan.Tray;

/// <summary>
/// `vman-tray --preview &lt;폴더&gt;` 로 실행하면 테마별 메뉴를 실제로 띄워
/// 화면을 그대로 캡처해 PNG 로 남긴다. 비율/색을 눈으로 보고 고치기 위한 도구.
/// </summary>
internal static class Preview
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var host = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0),
            Size = new Size(1, 1),
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Opacity = 0
        };

        host.Shown += async (_, _) =>
        {
            try
            {
                foreach (string appearance in new[] { "light", "dark" })
                foreach (var theme in Theme.All)
                    await CaptureAsync(theme, appearance, outDir);

                // 실제 트레이 위치(오른쪽 아래)에서 4단계까지 펼친 모습
                foreach (var theme in Theme.All)
                    await CaptureCornerAsync(theme, outDir);
            }
            finally
            {
                Application.Exit();
            }
        };

        Application.Run(host);
    }

    private static async Task CaptureAsync(Theme theme, string appearance, string outDir)
    {
        var app = new TrayApp(previewMode: true);
        var menu = app.BuildPreviewMenu(theme, appearance);

        var origin = new Point(60, 60);
        menu.Show(origin);
        await Task.Delay(500);

        Save(menu.Bounds, Path.Combine(outDir, $"{theme.Id}-{appearance}-root.png"));
        DumpMetrics(menu, theme, Path.Combine(outDir, $"{theme.Id}-{appearance}-metrics.txt"));

        // 첫 번째 도구(Python)의 서브메뉴까지 펼쳐서 한 장 더
        if (menu.Items.Count > 0 && menu.Items[0] is ToolStripMenuItem tool)
        {
            tool.Select();
            tool.ShowDropDown();
            await Task.Delay(500);

            var union = Rectangle.Union(menu.Bounds, tool.DropDown.Bounds);

            // 그 안의 "설치 가능" 서브메뉴도 한 겹 더 펼친다
            var nested = tool.DropDownItems.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.Text == "설치 가능");
            if (nested is not null)
            {
                nested.Select();
                nested.ShowDropDown();
                await Task.Delay(700);
                union = Rectangle.Union(union, nested.DropDown.Bounds);
            }

            Save(union, Path.Combine(outDir, $"{theme.Id}-{appearance}-sub.png"));
        }

        menu.Close();
        await Task.Delay(200);
        app.Dispose();
    }

    /// <summary>
    /// 트레이 아이콘 자리(작업 영역 오른쪽 아래)에서 메뉴를 띄우고
    /// 4단계까지 전부 펼쳐 화면 전체를 찍는다. 겹침/방향 뒤집힘을 보기 위한 것.
    /// </summary>
    private static async Task CaptureCornerAsync(Theme theme, string outDir)
    {
        var area = Screen.PrimaryScreen!.WorkingArea;

        // 커서를 트레이 자리로 옮겨야 ShouldDropLeft 가 실제 상황과 같게 판단한다
        Cursor.Position = new Point(area.Right - 30, area.Bottom - 10);

        var app = new TrayApp(previewMode: true);
        var menu = app.BuildPreviewMenu(theme, "light");

        menu.Show(new Point(area.Right - 30, area.Bottom - 10),
                  ToolStripDropDownDirection.AboveLeft);
        await Task.Delay(500);

        if (menu.Items.Count > 0 && menu.Items[0] is ToolStripMenuItem tool)
        {
            tool.Select();
            tool.ShowDropDown();
            await Task.Delay(500);

            var avail = tool.DropDownItems.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.Text == "설치 가능");
            if (avail is not null)
            {
                avail.Select();
                avail.ShowDropDown();
                await Task.Delay(900);

                if (avail.DropDownItems.OfType<ToolStripMenuItem>().FirstOrDefault() is { } group)
                {
                    group.Select();
                    group.ShowDropDown();
                    await Task.Delay(500);
                }
            }
        }

        Save(area, Path.Combine(outDir, $"{theme.Id}-corner.png"));

        menu.Close();
        await Task.Delay(200);
        app.Dispose();
    }

    /// <summary>레이아웃이 내 계산대로 잡혔는지 실제 수치로 확인한다.</summary>
    private static void DumpMetrics(ContextMenuStrip menu, Theme theme, string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"theme={theme.Id} MinWidth={theme.MinWidth} ArrowSpace={theme.ArrowSpace} RowPaddingX={theme.RowPaddingX}");
        sb.AppendLine($"menu.Width={menu.Width} ClientWidth={menu.ClientSize.Width} Padding={menu.Padding}");
        sb.AppendLine();

        foreach (ToolStripItem it in menu.Items)
        {
            if (it is not VmanMenuItem v) { sb.AppendLine($"  [sep] W={it.Width}"); continue; }
            var pref = v.GetPreferredSize(Size.Empty);
            sb.AppendLine(
                $"  '{v.Text}' W={v.Width} H={v.Height} pref={pref.Width}x{pref.Height} " +
                $"X={v.Bounds.X} auto={v.AutoSize} margin={v.Margin} pad={v.Padding} " +
                $"sec='{v.Secondary}' drop={v.HasDropDownItems} check={v.NeedsCheckColumn}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static void Save(Rectangle bounds, string path)
    {
        bounds.Inflate(12, 12);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            // 메뉴 밖 배경은 중간 회색으로 깔아 모서리 처리를 눈으로 볼 수 있게 한다
            g.Clear(Color.FromArgb(128, 128, 128));
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
