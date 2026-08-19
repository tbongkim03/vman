namespace VMan.Core;

/// <summary>
/// "고정 경로 하나가 실제 버전 폴더를 가리킨다"는 vman 의 핵심 장치.
/// 윈도우에서는 디렉터리 정션, 리눅스/WSL 에서는 심볼릭 링크로 구현한다.
///
/// 두 방식 모두 관리자/root 권한이 필요 없다는 점이 같다.
/// (윈도우 심볼릭 링크는 권한이 필요해서 정션을 쓰지만, 리눅스 심볼릭 링크는 그냥 만들 수 있다.)
/// </summary>
public static class Links
{
    /// <summary>해당 경로가 링크(정션/심볼릭 링크)인지. 끊어진 링크도 true.</summary>
    public static bool IsLink(string path)
    {
        if (Platform.IsWindows) return Junction.IsLink(path);

        // LinkTarget 은 lstat 기반이라 대상이 사라진 끊어진 링크에서도 값을 돌려준다.
        try { return new DirectoryInfo(path).LinkTarget is not null; }
        catch (Exception) { return false; }
    }

    /// <summary>링크가 가리키는 대상 경로. 링크가 아니면 null.</summary>
    public static string? GetTarget(string linkPath)
    {
        if (Platform.IsWindows) return Junction.GetTarget(linkPath);

        try
        {
            string? target = new DirectoryInfo(linkPath).LinkTarget;
            if (target is null) return null;
            // vman 은 항상 절대 경로로 만들지만, 손으로 만든 상대 링크도 받아준다.
            return Path.IsPathRooted(target)
                ? target
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath) ?? ".", target));
        }
        catch (Exception) { return null; }
    }

    /// <summary>링크 생성. 이미 뭔가 있으면 먼저 Remove 를 호출할 것.</summary>
    public static void Create(string linkPath, string targetDir)
    {
        if (Platform.IsWindows) { Junction.Create(linkPath, targetDir); return; }

        targetDir = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException($"대상 폴더가 없습니다: {targetDir}");

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        Directory.CreateSymbolicLink(linkPath, targetDir);
    }

    /// <summary>
    /// 링크 제거. 대상 폴더의 내용물은 절대 건드리지 않는다.
    /// 경로가 링크가 아닌 실제 폴더면 예외를 던진다(사고 방지).
    /// </summary>
    public static void Remove(string linkPath)
    {
        if (Platform.IsWindows) { Junction.Remove(linkPath); return; }

        if (!IsLink(linkPath))
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath)) return;
            throw new IOException($"링크가 아닌 실제 폴더입니다. 안전을 위해 삭제하지 않습니다: {linkPath}");
        }

        // 심볼릭 링크에 대한 unlink 는 링크 자체만 지운다. 재귀 삭제가 아니다.
        try { Directory.Delete(linkPath); }
        catch (Exception) { File.Delete(linkPath); }
    }

    /// <summary>Remove 후 Create. 버전 전환의 실체는 이 한 줄이다.</summary>
    public static void Repoint(string linkPath, string targetDir)
    {
        Remove(linkPath);
        Create(linkPath, targetDir);
    }
}
