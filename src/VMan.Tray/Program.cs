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

        // 탐색기 우클릭 메뉴가 이쪽으로 들어온다.
        // 콘솔 앱(vman.exe)을 물리면 검은 창이 번쩍이고 결과도 못 보여주므로
        // 창 없는 이 실행 파일이 대신 처리하고 결과만 대화상자로 알린다.
        // 트레이 아이콘은 만들지 않고 그대로 끝낸다.
        if (args.Length >= 2 && args[0] == "--venv")
        {
            CreateVenv(args[1], args.Length >= 3 ? args[2] : VenvManager.DefaultName);
            return;
        }

        // 트레이 아이콘이 두 개 뜨지 않도록 단일 인스턴스 보장
        using var mutex = new Mutex(true, @"Local\vman-tray-singleton", out bool isFirst);
        if (!isFirst) return;

        Application.Run(new TrayApp());
    }

    /// <summary>탐색기 메뉴에서 부른 가상환경 생성. 결과를 대화상자로 알린다.</summary>
    private static void CreateVenv(string dir, string name)
    {
        try
        {
            var venv = VenvManager.Create(dir, name);
            MessageBox.Show(
                $"만들었습니다.\n\n{venv.Path}\n{VenvManager.Probe(venv)}\n\n" +
                $"터미널에서 활성화하려면 그 폴더에서:\n  vman activate",
                "vman 가상환경", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "vman 가상환경",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
