using System.Diagnostics;

namespace VMan.Core;

/// <summary>가상환경 하나.</summary>
/// <param name="Path">가상환경 폴더의 절대 경로.</param>
/// <param name="Name">폴더 이름 (pyenv, .pyenv 등).</param>
public sealed record Venv(string Path, string Name)
{
    /// <summary>실행 파일이 모여 있는 곳. 윈도우는 Scripts, 유닉스는 bin.</summary>
    public string BinDir => System.IO.Path.Combine(Path, Platform.IsWindows ? "Scripts" : "bin");

    public string PythonExe => System.IO.Path.Combine(
        BinDir, Platform.IsWindows ? "python.exe" : "python");

    /// <summary>venv 가 자기를 표시하려고 두는 파일. 이것으로 가상환경인지 판별한다.</summary>
    public string ConfigFile => System.IO.Path.Combine(Path, "pyvenv.cfg");

    public bool IsValid => File.Exists(ConfigFile) && File.Exists(PythonExe);
}

/// <summary>
/// 디렉터리별 가상환경. "이 프로젝트 폴더에서 pip 이 딴 데를 건드리지 않게" 하는 장치다.
///
/// pyenv 를 붙이지 않는 이유는 단순하다. pyenv 는 <b>버전</b> 관리자라 vman 과 하는 일이
/// 같고, 둘을 같이 깔면 PATH 앞자리를 두고 다툰다. 반면 패키지를 폴더 단위로 가르는 것은
/// 파이썬에 들어 있는 venv 모듈이 하는 일이다. 그래서 vman 은 버전 전환을 맡고,
/// 폴더별 격리는 venv 에 맡긴 뒤 그 둘을 이어 주기만 한다.
///
/// 만들 때는 현재 vman 이 가리키는 파이썬을 쓴다. 즉 `vman use python 3.12.14` 뒤에
/// 만든 가상환경은 3.12.14 를 물려받는다.
/// </summary>
public static class VenvManager
{
    /// <summary>탐색기 메뉴와 CLI 가 함께 쓰는 기본 이름들. 첫 번째가 기본값.</summary>
    public static readonly IReadOnlyList<string> SuggestedNames = new[] { ".pyenv", "pyenv" };

    public static string DefaultName => SuggestedNames[0];

    /// <summary>
    /// 지정한 폴더에 가상환경을 만든다.
    /// 이름이 점으로 시작하면 윈도우에서도 실제로 숨긴다 — 윈도우는 점 접두어가 아니라
    /// 숨김 <b>속성</b>으로 감추기 때문에, 점만 붙여서는 탐색기에 그대로 보인다.
    /// </summary>
    public static Venv Create(string parentDir, string name, IProgress<string>? log = null)
    {
        parentDir = Path.GetFullPath(parentDir);
        if (!Directory.Exists(parentDir))
            throw new DirectoryNotFoundException($"폴더가 없습니다: {parentDir}");

        ValidateName(name);

        string python = ActivePython()
            ?? throw new InvalidOperationException(
                "쓸 파이썬이 지정되어 있지 않습니다. 먼저 `vman use python <버전>` 을 실행하세요.");

        string target = Path.Combine(parentDir, name);
        if (Directory.Exists(target))
        {
            var existing = new Venv(target, name);
            throw new IOException(existing.IsValid
                ? $"이미 가상환경이 있습니다: {target}"
                : $"같은 이름의 폴더가 이미 있습니다: {target}");
        }

        log?.Report($"가상환경을 만드는 중: {target}");
        log?.Report($"  기준 파이썬: {python}");

        RunPython(python, new[] { "-m", "venv", target });

        var venv = new Venv(target, name);
        if (!venv.IsValid)
            throw new InvalidOperationException(
                $"가상환경이 제대로 만들어지지 않았습니다: {target}");

        if (name.StartsWith('.')) HideOnWindows(target);

        log?.Report("완료.");
        return venv;
    }

    /// <summary>
    /// 이 폴더(또는 위쪽 폴더)에 있는 가상환경을 찾는다.
    /// 프로젝트 하위 폴더에서 명령을 실행해도 잡히도록 위로 거슬러 올라간다.
    /// </summary>
    public static Venv? Find(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));

        while (dir is not null)
        {
            // 흔한 이름을 먼저 보고, 없으면 그 폴더의 하위를 훑는다.
            foreach (string name in SuggestedNames.Concat(new[] { ".venv", "venv", "env" }))
            {
                var candidate = new Venv(Path.Combine(dir.FullName, name), name);
                if (candidate.IsValid) return candidate;
            }

            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// 이름이나 경로로 가상환경 하나를 정확히 집는다.
    /// Find 와 달리 추측하지 않는다. "방금 만든 그것"을 켜야 할 때 쓴다.
    /// </summary>
    public static Venv? Resolve(string baseDir, string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;

        string full = Path.IsPathRooted(nameOrPath)
            ? Path.GetFullPath(nameOrPath)
            : Path.GetFullPath(Path.Combine(baseDir, nameOrPath));

        var venv = new Venv(full, Path.GetFileName(full.TrimEnd('\\', '/')));
        return venv.IsValid ? venv : null;
    }

    /// <summary>현재 활성화된 가상환경. 없으면 null.</summary>
    public static Venv? Active()
    {
        string? path = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        if (string.IsNullOrWhiteSpace(path)) return null;

        var venv = new Venv(path, Path.GetFileName(path.TrimEnd('\\', '/')));
        return venv.IsValid ? venv : null;
    }

    /// <summary>가상환경이 어떤 파이썬을 기준으로 만들어졌는지 실행해서 확인한다.</summary>
    public static string Probe(Venv venv)
    {
        if (!venv.IsValid) return "(망가진 가상환경)";
        try
        {
            var psi = new ProcessStartInfo(venv.PythonExe, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return output.Split('\n').FirstOrDefault()?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            return $"(실행 실패: {ex.Message})";
        }
    }

    // ---------- 내부 ----------

    /// <summary>vman 이 지금 가리키는 파이썬. 링크를 거쳐야 전환에 따라 바뀐다.</summary>
    private static string? ActivePython()
    {
        string exe = Path.Combine(Layout.CurrentLink(ToolDef.Python), ToolDef.Python.ProbeExe);
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>폴더 이름으로 쓸 수 없는 것을 막는다. 탐색기 메뉴에서도 이 검사를 탄다.</summary>
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("가상환경 이름이 비어 있습니다.");

        if (name is "." or ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new ArgumentException($"가상환경 이름으로 쓸 수 없습니다: {name}");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"이름에 쓸 수 없는 문자가 있습니다: {name}");
    }

    private static void RunPython(string python, string[] args)
    {
        var psi = new ProcessStartInfo(python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException("파이썬을 실행하지 못했습니다.");

        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"가상환경 생성에 실패했습니다 (종료 코드 {p.ExitCode}).\n{stderr.Trim()}{stdout.Trim()}");
    }

    /// <summary>
    /// 윈도우에서 숨김 속성을 건다.
    /// 리눅스는 점 접두어만으로 숨겨지므로 할 일이 없다.
    /// </summary>
    private static void HideOnWindows(string dir)
    {
        if (!Platform.IsWindows) return;
        try
        {
            var info = new DirectoryInfo(dir);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception)
        {
            // 숨김에 실패해도 가상환경 자체는 멀쩡하다
        }
    }
}
