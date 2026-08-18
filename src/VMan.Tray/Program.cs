using VMan.Core;

namespace VMan.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Layout.EnsureDirectories();

        // 개발용: 테마별 메뉴를 캡처해 PNG 로 남긴다
        if (args.Length >= 2 && args[0] == "--preview")
        {
            Preview.Run(args[1]);
            return;
        }

        // 트레이 아이콘이 두 개 뜨지 않도록 단일 인스턴스 보장
        using var mutex = new Mutex(true, @"Local\vman-tray-singleton", out bool isFirst);
        if (!isFirst) return;

        Application.Run(new TrayApp());
    }
}
