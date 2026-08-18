using System.Diagnostics;
using Microsoft.Win32;
using VMan.Core;
using VMan.Tray.Theming;

namespace VMan.Tray;

/// <summary>
/// 작업 표시줄 알림 영역 상주 앱.
/// 메뉴는 열릴 때마다 다시 만든다 — CLI로 바꿔도 트레이가 항상 최신 상태를 보여준다.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "VManTray";

    private readonly NotifyIcon _icon;
    private readonly MenuHost _host;
    private readonly ContextMenuStrip _menu;
    private readonly Settings _settings;
    private readonly HashSet<string> _installing = new(StringComparer.OrdinalIgnoreCase);

    private Theme _theme;
    private Icon _currentIcon;

    public TrayApp() : this(previewMode: false) { }

    public TrayApp(bool previewMode)
    {
        _settings = Settings.Load();
        _theme = Theme.ById(_settings.Theme);

        _host = new MenuHost(() => _theme, IsDark);
        _menu = _host.CreateRoot();
        _menu.Opening += (_, _) => RebuildMenu();

        _currentIcon = IconFactory.Build(_theme);
        _icon = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = !previewMode,
            Text = "vman",
            ContextMenuStrip = _menu
        };

        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        UpdateTooltip();
        if (previewMode) return;

        // 아이콘이 등록된 뒤에야 레지스트리 항목이 생긴다
        _ = PromoteSoonAsync();

        // 메뉴를 처음 열 때 기다리지 않도록 목록을 미리 받아둔다
        _ = PrefetchAsync();
    }

    /// <summary>미리보기 하네스가 특정 테마의 메뉴를 통째로 얻어갈 때 쓴다.</summary>
    public ContextMenuStrip BuildPreviewMenu(Theme theme, string appearance)
    {
        _theme = theme;
        _settings.Appearance = appearance;
        RebuildMenu();
        return _menu;
    }

    public MenuHost Host => _host;

    private bool IsDark() => _settings.Appearance switch
    {
        "light" => false,
        "dark" => true,
        _ => Native.IsSystemDark()
    };

    private async Task PromoteSoonAsync()
    {
        await Task.Delay(1500);
        try { TrayPromotion.SetPromoted(true); } catch (Exception) { /* 무시 */ }
    }

    private async Task PrefetchAsync()
    {
        foreach (var tool in ToolDef.All)
        {
            try { await VersionCatalog.GetAsync(tool); }
            catch (Exception) { /* 오프라인이면 메뉴에서 다시 시도한다 */ }
        }
    }

    private void ShowMenu()
    {
        var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        mi?.Invoke(_icon, null);
    }

    // ---------- 메뉴 구성 ----------

    private VmanMenuItem Item(string text, string? secondary = null, bool marked = false,
                              bool header = false, bool compact = false)
        => new(text)
        {
            Secondary = secondary,
            Marked = marked,
            IsHeader = header,
            Compact = compact,
            Theme = _theme,
            Enabled = !header
        };

    /// <summary>
    /// 커서(=메뉴가 뜰 자리) 오른쪽에 서브메뉴 두 겹이 들어갈 자리가 없으면
    /// 전부 왼쪽으로 펴서 방향을 일관되게 만든다.
    /// </summary>
    private bool ShouldDropLeft()
    {
        var pt = Cursor.Position;
        var area = Screen.FromPoint(pt).WorkingArea;
        int needed = _theme.MinWidth * 2 + _theme.CompactMinWidth;
        return pt.X + needed > area.Right;
    }

    private void RebuildMenu()
    {
        _host.PreferLeft = ShouldDropLeft();
        _host.Style(_menu);
        _menu.Items.Clear();

        foreach (var tool in ToolDef.All)
            _menu.Items.Add(BuildToolMenu(tool));

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(BuildAppearanceMenu());

        var openFolder = Item("설치 폴더 열기");
        openFolder.Click += (_, _) => OpenInExplorer(Layout.VersionsDir);
        _menu.Items.Add(openFolder);

        bool startup = IsStartupEnabled();
        var startupItem = Item("윈도우 시작 시 실행", marked: startup);
        startupItem.Click += (_, _) => { SetStartup(!startup); };
        _menu.Items.Add(startupItem);

        // IsPromoted 레지스트리 값은 빌드에 따라 탐색기가 무시한다(26200에서 확인).
        // 최선을 다해 써보되, 사용자에게는 확실히 동작하는 설정 페이지를 안내한다.
        var promoteItem = Item("트레이에 항상 표시", "설정 열기");
        promoteItem.Click += (_, _) =>
        {
            TrayPromotion.SetPromoted(true);
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
            Notify("「기타 시스템 트레이 아이콘」에서 vman 을 켜주세요.");
        };
        _menu.Items.Add(promoteItem);

        _menu.Items.Add(new ToolStripSeparator());

        var exit = Item("종료");
        exit.Click += (_, _) => ExitApp();
        _menu.Items.Add(exit);
    }

    private ToolStripMenuItem BuildToolMenu(ToolDef tool)
    {
        string? current = VersionManager.CurrentVersion(tool);
        var root = Item(tool.DisplayName, current ?? "미설정");

        var children = new List<ToolStripItem>();
        var installed = VersionManager.List(tool);

        children.Add(Item("설치됨", header: true));
        if (installed.Count == 0)
        {
            children.Add(Item("(없음)", header: true));
        }
        else
        {
            foreach (var v in installed)
            {
                var item = Item(v.Version, marked: v.IsCurrent);
                var captured = v;
                item.Click += (_, _) => SwitchTo(captured);
                children.Add(item);
            }
        }

        if (current is not null)
        {
            children.Add(new ToolStripSeparator());
            var unset = Item("지정 해제");
            unset.Click += (_, _) => Guard(() =>
            {
                VersionManager.Unset(tool);
                UpdateTooltip();
                Notify($"{tool.DisplayName} 지정을 해제했습니다.");
            });
            children.Add(unset);
        }

        children.Add(new ToolStripSeparator());
        children.Add(BuildAvailableMenu(tool));

        var openTool = Item("폴더 열기");
        openTool.Click += (_, _) => OpenInExplorer(Layout.ToolVersionsDir(tool));
        children.Add(openTool);

        _host.AttachSubmenu(root, children.ToArray());
        return root;
    }

    /// <summary>인터넷에서 받아온 설치 가능 목록. 그룹(메이저/마이너)으로 한 겹 더 접는다.</summary>
    private ToolStripMenuItem BuildAvailableMenu(ToolDef tool)
    {
        var root = Item("설치 가능");
        var placeholder = Item("불러오는 중…", header: true);
        _host.AttachSubmenu(root, placeholder);

        root.DropDownOpening += async (_, _) =>
        {
            if (root.Tag is string loaded && loaded == "ok") return;

            try
            {
                var versions = await VersionCatalog.GetAsync(tool);
                var installed = VersionManager.List(tool)
                    .Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);

                root.DropDownItems.Clear();

                if (versions.Count == 0)
                {
                    root.DropDownItems.Add(Item("(목록을 가져오지 못했습니다)", header: true));
                }
                else
                {
                    foreach (var group in versions.GroupBy(v => v.Group))
                    {
                        var groupItem = Item(group.Key, compact: true);
                        var entries = new List<ToolStripItem>();

                        foreach (var v in group)
                        {
                            bool already = installed.Contains(v.Id)
                                           || installed.Contains($"temurin-{v.Id}");
                            string? note = already ? "설치됨" : v.Badge;

                            var item = Item(v.Id, note, marked: already, compact: true);
                            item.Enabled = !already;
                            if (!already)
                            {
                                var captured = v.Id;
                                item.Click += (_, _) => InstallAsync(tool, captured);
                            }
                            entries.Add(item);
                        }

                        _host.AttachSubmenu(groupItem, entries.ToArray());
                        root.DropDownItems.Add(groupItem);
                    }
                }

                _host.Style(root.DropDown);
                root.Tag = "ok";
            }
            catch (Exception ex)
            {
                root.DropDownItems.Clear();
                root.DropDownItems.Add(Item($"오류: {ex.Message}", header: true));
            }
        };

        return root;
    }

    private ToolStripMenuItem BuildAppearanceMenu()
    {
        var root = Item("모양", _theme.Name);
        var children = new List<ToolStripItem>();

        foreach (var t in Theme.All)
        {
            var item = Item(t.Name, marked: t.Id == _theme.Id);
            var captured = t;
            item.Click += (_, _) => ApplyTheme(captured);
            children.Add(item);
        }

        children.Add(new ToolStripSeparator());

        foreach (var (id, label) in new[] { ("system", "시스템 설정 따름"), ("light", "밝게"), ("dark", "어둡게") })
        {
            var item = Item(label, marked: _settings.Appearance == id);
            string captured = id;
            item.Click += (_, _) =>
            {
                _settings.Appearance = captured;
                _settings.Save();
                RefreshVisuals();
            };
            children.Add(item);
        }

        _host.AttachSubmenu(root, children.ToArray());
        return root;
    }

    // ---------- 동작 ----------

    private void ApplyTheme(Theme theme)
    {
        _theme = theme;
        _settings.Theme = theme.Id;
        _settings.Save();
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        var old = _currentIcon;
        _currentIcon = IconFactory.Build(_theme);
        _icon.Icon = _currentIcon;
        old.Dispose();

        _host.Style(_menu);
        UpdateTooltip();
    }

    private void SwitchTo(InstalledVersion v)
    {
        if (v.IsCurrent) return;
        Guard(() =>
        {
            VersionManager.Use(v.Tool, v.Version);
            UpdateTooltip();
            Notify($"{v.Tool.DisplayName} → {v.Version}\n새로 여는 터미널부터 적용됩니다.");
        });
    }

    private async void InstallAsync(ToolDef tool, string version)
    {
        string key = $"{tool.Id}/{version}";
        if (!_installing.Add(key)) return;

        try
        {
            Notify($"{tool.DisplayName} {version} 을(를) 내려받는 중...");
            var log = new Progress<string>(s => _icon.Text = Truncate($"vman - {s}"));

            string path = tool.Id switch
            {
                "node" => await Downloader.InstallNodeAsync(version, log),
                "java" => await Downloader.InstallJavaAsync(version, log),
                "python" => await Downloader.InstallPythonAsync(version, log),
                _ => throw new NotSupportedException(tool.Id)
            };

            string installed = Path.GetFileName(path);
            VersionManager.Use(tool, installed);
            UpdateTooltip();
            Notify($"{tool.DisplayName} {installed} 설치 후 전환했습니다.\n새로 여는 터미널부터 적용됩니다.");
        }
        catch (Exception ex)
        {
            UpdateTooltip();
            ShowError(ex);
        }
        finally
        {
            _installing.Remove(key);
        }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateTooltip()
    {
        var parts = ToolDef.All.Select(t =>
            $"{t.DisplayName}: {VersionManager.CurrentVersion(t) ?? "-"}");
        _icon.Text = Truncate("vman\n" + string.Join("\n", parts));
    }

    /// <summary>NotifyIcon.Text 는 63자 제한이 있다.</summary>
    private static string Truncate(string s) => s.Length > 62 ? s[..62] : s;

    private void Notify(string message)
    {
        _icon.BalloonTipTitle = "vman";
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(3000);
    }

    private static void ShowError(Exception ex) =>
        MessageBox.Show(ex.Message, "vman 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    // ---------- 시작 프로그램 등록 ----------

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon.Dispose();
        ExitThread();
    }
}
