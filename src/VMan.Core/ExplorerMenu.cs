using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VMan.Core;

/// <summary>
/// 탐색기에서 폴더를 우클릭했을 때 뜨는 "가상환경 만들기" 메뉴. 윈도우 전용.
///
/// HKCU 아래에만 쓰므로 관리자 권한이 필요 없다. 두 곳에 등록한다.
///   Directory\shell             — 폴더 아이콘을 직접 우클릭
///   Directory\Background\shell  — 폴더 안 빈 공간을 우클릭
/// 두 경우에 탐색기가 넘겨주는 인자가 다르다(%1 vs %V). 그래서 따로 등록해야 한다.
///
/// 하위 메뉴는 MUIVerb + subcommands="" 조합으로 만든다. subcommands 를 빈 문자열로
/// 두면 탐색기가 그 키 아래 shell\ 하위키들을 펼쳐 준다.
///
/// 실행은 vman.exe 가 아니라 vman-tray.exe 에 맡긴다. 콘솔 앱을 물리면 검은 창이
/// 번쩍이고 결과도 못 보여주기 때문이다. 트레이는 WinExe 라 창이 뜨지 않는다.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ExplorerMenu
{
    private const string MenuKeyName = "vman_venv";
    private const string DirectoryKey = @"Software\Classes\Directory\shell";
    private const string BackgroundKey = @"Software\Classes\Directory\Background\shell";

    /// <summary>탐색기가 실제로 실행할 파일. 트레이가 없으면 CLI 로 떨어진다.</summary>
    private static (string Exe, bool IsTray) Launcher()
    {
        string tray = Path.Combine(Layout.Bin, "vman-tray.exe");
        if (File.Exists(tray)) return (tray, true);

        string cli = Path.Combine(Layout.Bin, "vman.exe");
        if (File.Exists(cli)) return (cli, false);

        throw new FileNotFoundException(
            $"{Layout.Bin} 에서 vman-tray.exe 도 vman.exe 도 찾지 못했습니다. 먼저 설치하세요.");
    }

    /// <summary>메뉴를 등록한다. 이미 있으면 최신 내용으로 덮어쓴다.</summary>
    public static void Install()
    {
        var (exe, isTray) = Launcher();

        foreach (var (root, argToken) in new[] { (DirectoryKey, "%1"), (BackgroundKey, "%V") })
            WriteMenu(root, exe, isTray, argToken);
    }

    /// <summary>메뉴를 제거한다.</summary>
    public static void Uninstall()
    {
        foreach (string root in new[] { DirectoryKey, BackgroundKey })
        {
            using var key = Registry.CurrentUser.OpenSubKey(root, writable: true);
            key?.DeleteSubKeyTree(MenuKeyName, throwOnMissingSubKey: false);
        }
    }

    /// <summary>메뉴가 등록되어 있는지.</summary>
    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{DirectoryKey}\{MenuKeyName}");
        return key is not null;
    }

    private static void WriteMenu(string root, string exe, bool isTray, string argToken)
    {
        using var parent = Registry.CurrentUser.CreateSubKey($@"{root}\{MenuKeyName}")
                           ?? throw new InvalidOperationException($"레지스트리 키를 만들 수 없습니다: {root}");

        parent.SetValue("MUIVerb", "vman 가상환경 만들기");
        parent.SetValue("Icon", exe + ",0");
        // 빈 문자열이어야 아래 shell\ 하위키가 펼쳐진다. 값이 없으면 하위 메뉴가 안 생긴다.
        parent.SetValue("subcommands", "");

        using var shell = parent.CreateSubKey("shell")
                          ?? throw new InvalidOperationException("하위 메뉴 키를 만들 수 없습니다.");

        // 하위키 이름이 정렬 순서를 정하므로 번호를 붙인다.
        int order = 0;
        foreach (string name in VenvManager.SuggestedNames)
        {
            string label = name.StartsWith('.') ? $"{name}  (숨김)" : name;
            using var item = shell.CreateSubKey($"{order:00}_{Sanitize(name)}")!;
            item.SetValue("MUIVerb", label);

            using var command = item.CreateSubKey("command")!;
            command.SetValue("", isTray
                ? $"\"{exe}\" --venv \"{argToken}\" \"{name}\""
                : $"\"{exe}\" venv --dir \"{argToken}\" \"{name}\"");
            order++;
        }
    }

    /// <summary>레지스트리 키 이름으로 쓸 수 있게 다듬는다.</summary>
    private static string Sanitize(string name)
        => new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
