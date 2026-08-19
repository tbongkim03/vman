namespace VMan.Core;

/// <summary>
/// 리눅스/WSL2 에서 환경변수를 다룬다. 윈도우의 <see cref="EnvManager"/> 에 대응한다.
///
/// 레지스트리 같은 중앙 저장소가 없으므로 셸이 시작할 때 읽는 rc 파일을 쓴다.
/// 다만 rc 파일을 직접 어지럽히지 않고, vman 이 관리하는 <c>$VMAN_ROOT/env.sh</c> 한 장을
/// 만든 뒤 rc 파일에는 그것을 읽어들이는 두 줄짜리 블록만 넣는다.
/// 이후 설정이 바뀌어도 env.sh 만 다시 쓰면 되고 rc 파일은 건드리지 않는다.
///
/// 윈도우판과 마찬가지로 PATH 문자열 자체는 setup 때 한 번만 정해지고,
/// 버전 전환은 심볼릭 링크만 바꾸므로 이미 열린 셸에도 즉시 반영된다.
/// </summary>
public static class ShellEnv
{
    private const string BeginMarker = "# >>> vman >>>";
    private const string EndMarker = "# <<< vman <<<";

    /// <summary>블록을 넣을 후보 rc 파일들. 존재하는 것만 건드린다.</summary>
    private static IEnumerable<string> CandidateRcFiles()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".bashrc");
        yield return Path.Combine(home, ".zshrc");
        yield return Path.Combine(home, ".profile");
    }

    /// <summary>env.sh 를 만들고 rc 파일에 읽어들이는 블록을 넣는다. 손댄 rc 파일 목록을 돌려준다.</summary>
    public static IReadOnlyList<string> Install()
    {
        Layout.EnsureDirectories();
        WriteEnvFile();

        var touched = new List<string>();
        var candidates = CandidateRcFiles().ToList();

        // 하나도 없는 최소 환경이면 ~/.profile 을 새로 만든다.
        if (!candidates.Any(File.Exists))
        {
            File.WriteAllText(candidates[^1], "");
        }

        foreach (string rc in candidates)
        {
            if (!File.Exists(rc)) continue;
            if (EnsureBlock(rc)) touched.Add(rc);
        }
        return touched;
    }

    /// <summary>rc 파일에서 vman 블록을 걷어내고 env.sh 를 지운다.</summary>
    public static IReadOnlyList<string> Uninstall()
    {
        var touched = new List<string>();
        foreach (string rc in CandidateRcFiles())
        {
            if (!File.Exists(rc)) continue;
            string original = File.ReadAllText(rc);
            string stripped = StripBlock(original);
            if (stripped == original) continue;

            Backup(rc);

            // setup 이 없는 셸을 위해 만들어 준 빈 rc 라면 파일째 치운다.
            if (stripped.Trim().Length == 0) File.Delete(rc);
            else File.WriteAllText(rc, stripped);

            touched.Add(rc);
        }

        if (File.Exists(Layout.ShellEnvFile)) File.Delete(Layout.ShellEnvFile);
        return touched;
    }

    /// <summary>rc 파일에 vman 블록이 들어 있는지.</summary>
    public static bool IsInstalledIn(string rcFile)
        => File.Exists(rcFile) && File.ReadAllText(rcFile).Contains(BeginMarker, StringComparison.Ordinal);

    /// <summary>vman 블록을 가진 rc 파일 목록.</summary>
    public static IReadOnlyList<string> InstalledRcFiles()
        => CandidateRcFiles().Where(IsInstalledIn).ToList();

    /// <summary>
    /// env.sh 를 (다시) 쓴다.
    /// PATH 에 이미 있으면 건너뛰므로 여러 번 source 해도 PATH 가 부풀지 않는다.
    /// POSIX sh 문법만 쓴다 — dash 가 읽는 ~/.profile 에도 들어가기 때문이다.
    /// </summary>
    public static void WriteEnvFile()
    {
        Directory.CreateDirectory(Layout.Root);

        var sb = new System.Text.StringBuilder();
        sb.Append("""
            # vman - 이 파일은 vman 이 관리합니다. 직접 고치면 다음 setup 에서 덮어씁니다.
            # 사람이 손댈 곳이 아니라 `vman setup` 이 다시 만드는 산출물입니다.

            _vman_prepend() {
                # 이미 들어 있으면 그대로 둔다. 여러 번 읽혀도 PATH 가 길어지지 않는다.
                case ":${PATH}:" in
                    *":$1:"*) ;;
                    *) PATH="$1${PATH:+:${PATH}}" ;;
                esac
            }


            """);

        sb.AppendLine($"VMAN_ROOT=\"{Layout.Root}\"");
        sb.AppendLine("export VMAN_ROOT");
        sb.AppendLine();

        // 뒤에서부터 prepend 하면 최종 순서가 AllPathEntries 순서와 같아진다.
        foreach (string entry in Layout.AllPathEntries().Reverse())
            sb.AppendLine($"_vman_prepend \"{ToShellPath(entry)}\"");

        sb.AppendLine();
        sb.AppendLine("export PATH");
        sb.AppendLine();

        foreach (var tool in ToolDef.All)
        {
            if (tool.HomeEnvVar is null) continue;
            // 버전이 아니라 링크 자신을 가리키므로 전환해도 값이 변하지 않는다.
            // 링크가 없을 때(= 지정 해제 상태) 굳이 깨진 경로를 내보내지 않는다.
            string link = ToShellPath(Layout.CurrentLink(tool));
            sb.AppendLine($"if [ -d \"{link}\" ]; then");
            sb.AppendLine($"    {tool.HomeEnvVar}=\"{link}\"");
            sb.AppendLine($"    export {tool.HomeEnvVar}");
            sb.AppendLine("fi");
        }

        sb.AppendLine();
        sb.AppendLine("unset -f _vman_prepend");
        sb.AppendLine();

        // 자동활성화 스위치. 훅은 항상 심고, 켜고 끄는 것은 이 변수로 한다.
        // 그래야 `vman autoactivate off` 가 지금 이 창에도 바로 먹는다.
        sb.AppendLine($"VMAN_AUTO_VENV={(Settings.Load().AutoActivateVenv ? 1 : 0)}");
        sb.AppendLine("export VMAN_AUTO_VENV");
        sb.AppendLine();
        sb.Append(AutoActivateHook());
        sb.AppendLine();
        sb.Append(WrapperFunction());

        File.WriteAllText(Layout.ShellEnvFile, sb.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// 폴더를 옮길 때마다 그 폴더의 가상환경을 켜고 끄는 훅.
    ///
    /// 프롬프트를 그릴 때마다 도는 코드라 비싸면 안 된다. 그래서
    ///   - 디렉터리가 그대로면 즉시 빠져나온다 (대부분의 프롬프트가 여기서 끝난다)
    ///   - 가상환경 탐색은 셸 안에서 문자열 조작으로만 한다. 프로세스를 띄우지 않는다
    ///   - vman 을 부르는 것은 대상이 <b>실제로 바뀌었을 때</b>뿐이다
    ///
    /// 손으로 켠 가상환경은 건드리지 않는다. 훅이 켠 것에만 _VMAN_AUTO_SET 표시를 남기고,
    /// 그 표시가 있을 때만 훅이 다시 끌 수 있다. 그렇지 않으면 `vman activate` 로
    /// 딴 폴더 것을 켜 둔 사람이 프롬프트 한 번에 그것을 잃는다.
    /// </summary>
    private static string AutoActivateHook() => """
        # 폴더를 옮기면 그 폴더의 가상환경을 자동으로 켠다. VMAN_AUTO_VENV=0 이면 아무 일도 안 한다.
        _vman_auto_venv() {
            [ "${VMAN_AUTO_VENV:-0}" = "1" ] || return 0
            [ "$PWD" = "${_VMAN_LAST_PWD:-}" ] && return 0
            _VMAN_LAST_PWD="$PWD"

            # 손으로 켠 것은 건드리지 않는다
            if [ -n "${VIRTUAL_ENV:-}" ] && [ "${_VMAN_AUTO_SET:-0}" != "1" ]; then
                return 0
            fi

            # 위로 거슬러 올라가며 pyvenv.cfg 를 찾는다. 프로세스를 띄우지 않는다.
            _vman_found=""
            _vman_d="$PWD"
            while : ; do
                for _vman_n in .venv venv env .pyenv pyenv; do
                    if [ -f "$_vman_d/$_vman_n/pyvenv.cfg" ]; then
                        _vman_found="$_vman_d/$_vman_n"
                        break
                    fi
                done
                [ -n "$_vman_found" ] && break
                [ -z "$_vman_d" ] || [ "$_vman_d" = "/" ] && break
                _vman_d="${_vman_d%/*}"
            done

            if [ "$_vman_found" != "${VIRTUAL_ENV:-}" ]; then
                if [ -n "$_vman_found" ]; then
                    eval "$(VMAN_SHELL=posix "${VMAN_ROOT}/bin/vman" env --shell posix --activate "$_vman_found" 2>/dev/null)"
                    _VMAN_AUTO_SET=1
                else
                    eval "$(VMAN_SHELL=posix "${VMAN_ROOT}/bin/vman" env --shell posix --deactivate 2>/dev/null)"
                    _VMAN_AUTO_SET=0
                fi
                hash -r 2>/dev/null || true
            fi

            unset _vman_found _vman_d _vman_n
            return 0
        }

        # 프롬프트 앞에 (이름) 을 붙여 가상환경이 켜졌는지 눈으로 알 수 있게 한다.
        # 표준 activate 스크립트가 하는 일과 같다. VMAN_VENV_PROMPT=0 이면 끈다.
        #
        # 원본 PS1 을 한 번만 보관해 두고 매번 그것에서 다시 만든다. 앞에 덧붙이기만
        # 하면 가상환경을 옮겨 다닐 때 (a) (b) (c) 처럼 접두어가 쌓인다.
        _vman_venv_ps1() {
            [ "${VMAN_VENV_PROMPT:-1}" = "1" ] || return 0
            [ -n "${_VMAN_PS1_BASE+x}" ] || _VMAN_PS1_BASE="$PS1"

            if [ -n "${VIRTUAL_ENV:-}" ]; then
                PS1="(${VIRTUAL_ENV_PROMPT:-${VIRTUAL_ENV##*/}}) $_VMAN_PS1_BASE"
            else
                PS1="$_VMAN_PS1_BASE"
            fi
            return 0
        }

        _vman_prompt() {
            _vman_auto_venv
            _vman_venv_ps1
            return 0
        }

        # 프롬프트를 그리기 직전에 부른다. 셸마다 거는 자리가 다르다.
        if [ -n "${ZSH_VERSION:-}" ]; then
            typeset -ga precmd_functions
            case " ${precmd_functions[*]} " in
                *" _vman_prompt "*) ;;
                *) precmd_functions+=(_vman_prompt) ;;
            esac
        elif [ -n "${BASH_VERSION:-}" ]; then
            case "${PROMPT_COMMAND:-}" in
                *_vman_prompt*) ;;
                *) PROMPT_COMMAND="_vman_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}" ;;
            esac
        fi

        """;

    /// <summary>
    /// 실제 실행 파일을 감싸는 셸 함수.
    ///
    /// 이것이 "한 창에서 연속으로" 를 가능하게 하는 부분이다.
    /// 프로세스는 부모 셸의 환경을 못 바꾸지만, 셸 함수는 셸 안에서 도니까 바꿀 수 있다.
    /// setup / unsetup 뒤에 `vman env` 가 뱉은 대입문을 eval 해서 지금 창에 바로 반영한다.
    ///
    /// use 뒤에는 hash -r 을 부른다. PATH 는 그대로라 전환 자체는 즉시 먹지만,
    /// 셸이 예전에 다른 곳(/usr/bin/python3 같은)으로 잡아 둔 캐시가 남아 있으면
    /// 그것이 계속 이긴다. 캐시만 비워 주면 된다.
    /// </summary>
    private static string WrapperFunction() => """
        # vman 을 감싸는 함수. 환경을 바꾸는 명령 뒤에 이 셸에 곧바로 반영한다.
        vman() {
            _vman_bin="${VMAN_ROOT}/bin/vman"
            [ -x "$_vman_bin" ] || _vman_bin="$(command -v vman 2>/dev/null)"
            if [ -z "$_vman_bin" ]; then
                echo "vman 실행 파일을 찾을 수 없습니다: ${VMAN_ROOT}/bin/vman" >&2
                unset _vman_bin
                return 127
            fi

            VMAN_SHELL="posix" "$_vman_bin" "$@"
            _vman_status=$?

            # 종료 코드로 거르지 않는다. doctor 는 문제를 발견하면 1을 돌려주는데,
            # 그 문제가 바로 "이 창이 낡았다" 인 경우가 있어서 여기서 걸러 버리면
            # 고치려던 것을 영영 못 고친다. env 가 실패하면 출력이 비어 eval 이 무해하다.
            if true; then
                case "$1" in
                    setup)
                        eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix)" ;;
                    unsetup)
                        eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --revert)" ;;
                    autoactivate|auto)
                        eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --auto)" ;;
                    reload)
                        eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --reload)"
                        hash -r 2>/dev/null || true ;;
                    doctor)
                        # --fix 를 준 경우에만. 그냥 진단할 때 환경을 건드리면 안 된다.
                        case " $* " in
                            *" --fix "*|*" -f "*)
                                eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --reload)"
                                hash -r 2>/dev/null || true ;;
                        esac ;;
                    venv|activate)
                        # 이름을 그대로 넘긴다. 안 넘기면 Find 가 고정 순서로 골라서
                        # 방금 만든 것과 다른 가상환경이 켜질 수 있다.
                        if [ -n "$2" ]; then
                            eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --activate "$2" 2>/dev/null)"
                        else
                            eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --activate 2>/dev/null)"
                        fi
                        hash -r 2>/dev/null || true ;;
                    deactivate)
                        eval "$(VMAN_SHELL=posix "$_vman_bin" env --shell posix --deactivate)"
                        hash -r 2>/dev/null || true ;;
                    use|unset|install|import|remove|rm)
                        # 셸이 캐시해 둔 예전 경로를 비운다
                        hash -r 2>/dev/null || true ;;
                esac
            fi

            unset _vman_bin
            return $_vman_status
        }

        """;

    /// <summary>$VMAN_ROOT 로 시작하는 경로는 변수로 줄여 쓴다(루트를 옮겨도 읽기 쉽게).</summary>
    private static string ToShellPath(string absolute)
        => absolute.StartsWith(Layout.Root, StringComparison.Ordinal)
            ? "${VMAN_ROOT}" + absolute[Layout.Root.Length..]
            : absolute;

    /// <summary>rc 파일에 블록이 없으면 끝에 붙인다. 이미 있으면 내용을 최신으로 맞춘다.</summary>
    private static bool EnsureBlock(string rcFile)
    {
        string block = string.Join("\n", new[]
        {
            BeginMarker,
            $"[ -f \"{Layout.ShellEnvFile}\" ] && . \"{Layout.ShellEnvFile}\"",
            EndMarker
        });

        string original = File.ReadAllText(rcFile);
        string stripped = StripBlock(original).TrimEnd('\n');
        string updated = stripped.Length == 0
            ? block + "\n"
            : stripped + "\n\n" + block + "\n";

        if (updated == original) return false;

        Backup(rcFile);
        File.WriteAllText(rcFile, updated);
        return true;
    }

    /// <summary>마커 사이(마커 포함)를 잘라낸다. 마커가 없으면 원본 그대로.</summary>
    private static string StripBlock(string content)
    {
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

        return string.Join("\n", kept).TrimEnd('\n') + "\n";
    }

    private static void Backup(string file)
    {
        Directory.CreateDirectory(Layout.BackupDir);
        string name = Path.GetFileName(file).TrimStart('.');
        File.Copy(file,
            Path.Combine(Layout.BackupDir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.bak"),
            overwrite: true);
    }
}
