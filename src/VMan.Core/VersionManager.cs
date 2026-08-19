using System.Diagnostics;

namespace VMan.Core;

public sealed record InstalledVersion(ToolDef Tool, string Version, string Path, bool IsCurrent);

public static class VersionManager
{
    /// <summary>최초 1회. 폴더를 만들고 PATH에 고정 경로를 등록한다.</summary>
    /// <param name="force">
    /// 이미 등록되어 있어도 vman 경로를 PATH 맨 앞으로 다시 끌어올린다.
    /// 다른 설치 프로그램(대표적으로 마이크로소프트 스토어 Python)이 자기 경로를
    /// PATH 앞에 끼워 넣어 vman 이 가려졌을 때 쓴다.
    /// </param>
    public static EnvSetupResult Setup(bool force = false)
    {
        Layout.EnsureDirectories();

        // 링크 대상이 아직 없어도 PATH에 미리 넣어둔다.
        // 존재하지 않는 PATH 항목은 그냥 무시되므로 안전하다.
        return EnvStore.Setup(force);
    }

    /// <summary>vman이 건드린 PATH와 JAVA_HOME을 되돌린다. versions 폴더는 남긴다.</summary>
    public static IReadOnlyList<string> Unsetup() => EnvStore.Unsetup();

    /// <summary>versions\{tool} 아래 실제로 쓸 수 있는 버전 목록.</summary>
    public static IReadOnlyList<InstalledVersion> List(ToolDef tool)
    {
        string dir = Layout.ToolVersionsDir(tool);
        if (!Directory.Exists(dir)) return Array.Empty<InstalledVersion>();

        string? current = CurrentVersion(tool);

        return Directory.EnumerateDirectories(dir)
            .Where(d => File.Exists(System.IO.Path.Combine(d, tool.ProbeExe)))
            .Select(d => new InstalledVersion(
                tool,
                System.IO.Path.GetFileName(d),
                d,
                string.Equals(System.IO.Path.GetFileName(d), current, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.Version, VersionComparer.Instance)
            .ToList();
    }

    /// <summary>현재 활성 버전 이름. 링크가 없으면 null.</summary>
    public static string? CurrentVersion(ToolDef tool)
    {
        string? target = Links.GetTarget(Layout.CurrentLink(tool));
        if (target is null) return null;
        return System.IO.Path.GetFileName(target.TrimEnd('\\', '/'));
    }

    /// <summary>버전 전환. 링크를 다시 걸고 부속 환경변수를 갱신한다.</summary>
    public static void Use(ToolDef tool, string version)
    {
        string target = Layout.VersionDir(tool, version);
        if (!Directory.Exists(target))
            throw new DirectoryNotFoundException($"{tool.DisplayName} {version} 이(가) 설치되어 있지 않습니다.");
        if (!File.Exists(System.IO.Path.Combine(target, tool.ProbeExe)))
            throw new FileNotFoundException($"유효한 설치본이 아닙니다. {tool.ProbeExe} 를 찾을 수 없습니다: {target}");

        Directory.CreateDirectory(Layout.CurrentDir);
        Links.Repoint(Layout.CurrentLink(tool), target);

        EnvStore.SetToolHome(tool);
        EnvStore.Broadcast();
    }

    /// <summary>현재 지정을 해제한다(링크 제거).</summary>
    public static void Unset(ToolDef tool)
    {
        Links.Remove(Layout.CurrentLink(tool));
        EnvStore.ClearToolHome(tool);
        EnvStore.Broadcast();
    }

    /// <summary>
    /// 이미 설치되어 있는 런타임을 vman이 관리하도록 등록한다.
    /// 파일을 복사하지 않고 링크로 연결하므로 즉시 끝나고 디스크도 안 먹는다.
    /// </summary>
    public static void Import(ToolDef tool, string version, string sourcePath)
    {
        sourcePath = System.IO.Path.GetFullPath(sourcePath);
        if (!File.Exists(System.IO.Path.Combine(sourcePath, tool.ProbeExe)))
            throw new FileNotFoundException(
                $"{sourcePath} 에서 {tool.ProbeExe} 를 찾을 수 없습니다. 경로를 확인하세요.");

        string dest = Layout.VersionDir(tool, version);
        if (Directory.Exists(dest))
            throw new IOException($"이미 {version} 이(가) 등록되어 있습니다.");

        Directory.CreateDirectory(Layout.ToolVersionsDir(tool));
        Links.Create(dest, sourcePath);
    }

    /// <summary>설치본 삭제. 현재 사용 중이면 먼저 해제한다.</summary>
    public static void Remove(ToolDef tool, string version)
    {
        string dir = Layout.VersionDir(tool, version);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"{tool.DisplayName} {version} 을(를) 찾을 수 없습니다.");

        if (string.Equals(CurrentVersion(tool), version, StringComparison.OrdinalIgnoreCase))
            Unset(tool);

        if (Links.IsLink(dir)) Links.Remove(dir);      // import 로 등록한 것 → 링크만 끊음
        else Directory.Delete(dir, recursive: true);   // 실제 설치본 → 통째로 삭제
    }

    /// <summary>현재 활성 버전이 실제로 무엇을 보고하는지 실행해서 확인.</summary>
    public static string Probe(ToolDef tool)
    {
        string exe = System.IO.Path.Combine(Layout.CurrentLink(tool), tool.ProbeExe);
        if (!File.Exists(exe)) return "(설정 안 됨)";

        try
        {
            var psi = new ProcessStartInfo(exe, tool.Id == "java" ? "-version" : "--version")
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
}

/// <summary>"3.12.4" 를 문자열이 아니라 숫자로 비교하기 위한 정렬기.</summary>
internal sealed class VersionComparer : IComparer<string>
{
    public static readonly VersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var a = Parse(x); var b = Parse(y);
        for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            int av = i < a.Count ? a[i] : 0;
            int bv = i < b.Count ? b[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static List<int> Parse(string? s)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(s)) return result;
        foreach (var part in s.Split('.', '-', '+', '_'))
            if (int.TryParse(part, out int n)) result.Add(n);
        return result;
    }
}
