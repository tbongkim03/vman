using System.Runtime.Versioning;
using System.Text;

namespace VMan.Core;

/// <summary>
/// 윈도우 PowerShell 쪽 연동. 리눅스 <see cref="ShellEnv"/> 와 정확히 대칭이다.
///
/// 레지스트리(<see cref="EnvManager"/>)만으로도 <b>새</b> 프로세스는 다 챙겨진다.
/// 이것을 따로 두는 이유는 두 가지다.
///
/// 1. <b>지금 이 창</b>에 바로 반영하려면 셸 안에서 도는 무언가가 필요하다.
///    프로세스는 부모 셸의 환경을 못 바꾸지만 PowerShell 함수는 바꿀 수 있다.
/// 2. `vman setup` 을 실행한 그 창에서 곧바로 python 을 쳐 보는 것이 사람의 자연스러운
///    행동인데, 레지스트리만 고쳐서는 그 창이 절대 갱신되지 않는다.
///
/// 레지스트리 수정은 그대로 둔다. GUI 앱이나 PowerShell 이 아닌 프로그램은 그쪽만 본다.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PowerShellEnv
{
    private const string BeginMarker = "# >>> vman >>>";
    private const string EndMarker = "# <<< vman <<<";

    /// <summary>$VMAN_ROOT\env.ps1 — 프로필이 읽어들이는 실제 내용.</summary>
    public static string EnvFile => Path.Combine(Layout.Root, "env.ps1");

    /// <summary>
    /// 손볼 프로필 후보.
    /// Windows PowerShell 5.1 과 PowerShell 7 은 프로필 경로가 서로 다르다.
    /// MyDocuments 를 쓰면 OneDrive 로 리디렉션된 문서 폴더도 알아서 따라간다.
    /// </summary>
    private static IEnumerable<string> CandidateProfiles()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(docs, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        yield return Path.Combine(docs, "PowerShell", "Microsoft.PowerShell_profile.ps1");
    }

    /// <summary>env.ps1 을 쓰고 프로필에 읽어들이는 블록을 넣는다. 손댄 파일 목록을 돌려준다.</summary>
    public static IReadOnlyList<string> Install()
    {
        WriteEnvFile();

        var touched = new List<string>();
        foreach (string profile in CandidateProfiles())
        {
            string dir = Path.GetDirectoryName(profile)!;

            // PowerShell 7 은 설치되어 있지 않을 수 있다. 그 폴더까지 새로 만들지는 않는다.
            // 5.1 (첫 번째 후보)은 윈도우에 항상 있으므로 없으면 만든다.
            bool isWindowsPowerShell = dir.EndsWith("WindowsPowerShell", StringComparison.OrdinalIgnoreCase);
            if (!Directory.Exists(dir))
            {
                if (!isWindowsPowerShell) continue;
                Directory.CreateDirectory(dir);
            }

            if (EnsureBlock(profile)) touched.Add(profile);
        }
        return touched;
    }

    /// <summary>프로필에서 vman 블록을 걷어내고 env.ps1 을 지운다.</summary>
    public static IReadOnlyList<string> Uninstall()
    {
        var touched = new List<string>();
        foreach (string profile in CandidateProfiles())
        {
            if (!File.Exists(profile)) continue;
            string original = File.ReadAllText(profile);
            string stripped = StripBlock(original);
            if (stripped == original) continue;

            Backup(profile);

            // 우리가 만든 프로필에 vman 블록만 있었다면 파일째 치운다.
            // 빈 프로필을 남겨 두면 "내가 안 만든 파일"이 계속 남는다.
            if (stripped.Trim().Length == 0) File.Delete(profile);
            else File.WriteAllText(profile, stripped);

            touched.Add(profile);
        }

        if (File.Exists(EnvFile)) File.Delete(EnvFile);
        return touched;
    }

    /// <summary>vman 블록을 가진 프로필 목록.</summary>
    public static IReadOnlyList<string> InstalledProfiles()
        => CandidateProfiles()
            .Where(p => File.Exists(p)
                        && File.ReadAllText(p).Contains(BeginMarker, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// env.ps1 을 (다시) 쓴다.
    /// 이미 PATH 에 있으면 건너뛰므로 프로필이 여러 번 읽혀도 PATH 가 부풀지 않는다.
    /// </summary>
    public static void WriteEnvFile()
    {
        Directory.CreateDirectory(Layout.Root);

        var sb = new StringBuilder();
        sb.AppendLine("# vman - 이 파일은 vman 이 관리합니다. 직접 고치면 다음 setup 에서 덮어씁니다.");
        sb.AppendLine("# 사람이 손댈 곳이 아니라 `vman setup` 이 다시 만드는 산출물입니다.");
        sb.AppendLine();
        sb.AppendLine($"$env:VMAN_ROOT = {Quote(Layout.Root)}");
        sb.AppendLine();
        sb.AppendLine("function global:__vman_prepend([string] $p) {");
        sb.AppendLine("    # 이미 들어 있으면 그대로 둔다. 여러 번 읽혀도 PATH 가 길어지지 않는다.");
        sb.AppendLine("    if (($env:PATH -split ';') -notcontains $p) { $env:PATH = \"$p;$env:PATH\" }");
        sb.AppendLine("}");
        sb.AppendLine();

        // 뒤에서부터 붙이면 최종 순서가 AllPathEntries 순서와 같아진다.
        foreach (string entry in Layout.AllPathEntries().Reverse())
            sb.AppendLine($"__vman_prepend {Quote(entry)}");

        sb.AppendLine();
        sb.AppendLine("Remove-Item function:__vman_prepend -ErrorAction SilentlyContinue");
        sb.AppendLine();

        foreach (var tool in ToolDef.All)
        {
            if (tool.HomeEnvVar is null) continue;
            // 링크가 없을 때(= 지정 해제 상태) 깨진 경로를 내보내지 않는다.
            string link = Layout.CurrentLink(tool);
            sb.AppendLine($"if (Test-Path -LiteralPath {Quote(link)}) {{");
            sb.AppendLine($"    $env:{tool.HomeEnvVar} = {Quote(link)}");
            sb.AppendLine("}");
        }

        sb.AppendLine();
        sb.Append(WrapperFunction());

        File.WriteAllText(EnvFile, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// vman.exe 를 감싸는 PowerShell 함수.
    /// 함수는 셸 안에서 돌기 때문에 $env: 를 바꿀 수 있다. 이것이 "한 창에서 연속으로" 의 핵심이다.
    /// PowerShell 에서는 함수가 외부 실행 파일보다 우선하므로 `vman` 이 이 함수로 들어온다.
    /// </summary>
    private static string WrapperFunction() => """
        # vman 을 감싸는 함수. 환경을 바꾸는 명령 뒤에 이 창에 곧바로 반영한다.
        function global:vman {
            $exe = Join-Path $env:VMAN_ROOT 'bin\vman.exe'
            if (-not (Test-Path -LiteralPath $exe)) {
                $cmd = Get-Command vman.exe -CommandType Application -ErrorAction SilentlyContinue |
                       Select-Object -First 1
                if ($null -eq $cmd) {
                    Write-Error "vman.exe 를 찾을 수 없습니다: $env:VMAN_ROOT\bin\vman.exe"
                    return
                }
                $exe = $cmd.Source
            }

            $env:VMAN_SHELL = 'powershell'
            & $exe @args
            $code = $LASTEXITCODE

            # 종료 코드로 거르지 않는다. doctor 는 문제를 발견하면 1을 돌려주는데,
            # 그 문제가 바로 "이 창이 낡았다" 인 경우가 있기 때문이다.
            if ($args.Count -gt 0) {
                $apply = $null
                switch ($args[0]) {
                    'setup'   { $apply = @('env', '--shell', 'powershell') }
                    'unsetup' { $apply = @('env', '--shell', 'powershell', '--revert') }
                    'reload'  { $apply = @('env', '--shell', 'powershell', '--reload') }
                    { $_ -in 'venv', 'activate' } {
                        # 이름을 그대로 넘긴다. 안 넘기면 방금 만든 것과 다른 가상환경이 켜질 수 있다.
                        $apply = @('env', '--shell', 'powershell', '--activate')
                        if ($args.Count -gt 1 -and -not $args[1].ToString().StartsWith('-')) {
                            $apply += $args[1]
                        }
                    }
                    'deactivate' { $apply = @('env', '--shell', 'powershell', '--deactivate') }
                    'doctor'  {
                        # --fix 를 준 경우에만. 그냥 진단할 때 환경을 건드리면 안 된다.
                        if ($args -contains '--fix' -or $args -contains '-f') {
                            $apply = @('env', '--shell', 'powershell', '--reload')
                        }
                    }
                }
                if ($null -ne $apply) {
                    Invoke-Expression (((& $exe @apply) -join "`n"))
                }
            }

            $global:LASTEXITCODE = $code
        }

        """;

    // ---------- 프로필 블록 다루기 (ShellEnv 와 같은 규칙) ----------

    private static bool EnsureBlock(string profile)
    {
        string block = string.Join(Environment.NewLine, new[]
        {
            BeginMarker,
            $"if (Test-Path -LiteralPath {Quote(EnvFile)}) {{ . {Quote(EnvFile)} }}",
            EndMarker
        });

        string original = File.Exists(profile) ? File.ReadAllText(profile) : "";
        string stripped = StripBlock(original).TrimEnd('\r', '\n');
        string nl = Environment.NewLine;
        string updated = stripped.Length == 0
            ? block + nl
            : stripped + nl + nl + block + nl;

        if (updated == original) return false;

        if (File.Exists(profile)) Backup(profile);
        File.WriteAllText(profile, updated);
        return true;
    }

    /// <summary>마커 사이(마커 포함)를 잘라낸다. 마커가 없으면 원본 그대로.</summary>
    private static string StripBlock(string content)
    {
        if (content.Length == 0) return content;

        var lines = content.ReplaceLineEndings("\n").Split('\n').ToList();
        var kept = new List<string>();
        bool inside = false;

        foreach (string line in lines)
        {
            if (!inside && line.TrimStart().StartsWith(BeginMarker, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }
            if (inside)
            {
                if (line.TrimStart().StartsWith(EndMarker, StringComparison.Ordinal)) inside = false;
                continue;
            }
            kept.Add(line);
        }

        // 마커가 열린 채 끝났으면(손으로 지우다 만 경우) 원본을 건드리지 않는다.
        if (inside) return content;

        string result = string.Join(Environment.NewLine, kept).TrimEnd('\r', '\n');
        return result.Length == 0 ? "" : result + Environment.NewLine;
    }

    private static void Backup(string file)
    {
        Directory.CreateDirectory(Layout.BackupDir);
        File.Copy(file,
            Path.Combine(Layout.BackupDir,
                $"{Path.GetFileNameWithoutExtension(file)}-{DateTime.Now:yyyyMMdd-HHmmss}.bak"),
            overwrite: true);
    }

    private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
}
