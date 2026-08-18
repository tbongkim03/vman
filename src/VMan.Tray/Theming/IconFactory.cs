using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VMan.Tray.Theming;

/// <summary>
/// 트레이 아이콘을 코드로 그린다(.ico 리소스 없음).
/// 16×16 로 줄어들어도 뭉개지지 않도록 글자 대신 선으로 V 를 그린다.
/// </summary>
internal static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Build(Theme theme)
    {
        const int S = 32;
        using var bmp = new Bitmap(S, S);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // 테마에 따라 모서리 곡률을 바꾼다 (Apple 은 스퀘어클, One UI 는 더 둥글게)
            int radius = theme.Id == "oneui" ? 11 : 8;
            var body = new Rectangle(1, 1, S - 3, S - 3);

            (Color from, Color to) = theme.Id == "oneui"
                ? (Color.FromArgb(78, 137, 250), Color.FromArgb(11, 87, 208))
                : (Color.FromArgb(64, 156, 255), Color.FromArgb(0, 96, 223));

            using (var path = Native.RoundedRect(body, radius))
            using (var brush = new LinearGradientBrush(body, from, to, 62f))
                g.FillPath(brush, path);

            // 위쪽 하이라이트 — 살짝 입체감
            using (var path = Native.RoundedRect(body, radius))
            using (var gloss = new LinearGradientBrush(
                       new Rectangle(body.X, body.Y, body.Width, body.Height / 2),
                       Color.FromArgb(56, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                var clip = g.Clip;
                g.SetClip(path);
                g.FillRectangle(gloss, body.X, body.Y, body.Width, body.Height / 2);
                g.Clip = clip;
            }

            // V 글리프
            using var pen = new Pen(Color.White, 3.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(10.5f, 11f),
                new PointF(16f, 22f),
                new PointF(21.5f, 11f)
            });
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(h);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(h);   // GetHicon 이 만든 핸들은 반드시 되돌려준다
        }
    }
}
