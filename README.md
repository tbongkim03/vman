# vman

Python / Java / Node.js 버전 관리자. **윈도우와 리눅스(WSL2 포함)** 양쪽에서 돌아갑니다.
윈도우에서는 CLI와 트레이 메뉴 양쪽으로 전환할 수 있습니다.

- **관리자/root 권한 불필요** — 윈도우는 디렉터리 정션, 리눅스는 심볼릭 링크를 씁니다
- **PATH는 설치 시 한 번만** 손대고 이후로는 건드리지 않습니다
- **런타임을 재배포하지 않고** 공식 배포처에서 직접 받습니다
- .NET 8, MIT 라이선스

```
윈도우   %LOCALAPPDATA%\vman\current\python   ← PATH에 박히는 고정 경로 (정션)
리눅스   ~/.local/share/vman/current/python   ← 같은 역할 (심볼릭 링크)
                     │
                     └─→ versions/python/3.12.14   전환할 때 이 화살표만 바꿉니다
```

PATH 문자열이 바뀌지 않으므로 **이미 열려 있는 터미널에서도 전환이 바로 반영됩니다.**
`vman use` 뒤에 창을 새로 열 필요가 없습니다 — 그 자리에서 이어서 쓰면 됩니다.

## 설치

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

### 윈도우

```powershell
git clone https://github.com/<사용자명>/vman.git
cd vman
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

`dist\` 에 `vman.exe`(CLI)와 `vman-tray.exe`(트레이)가 생기고, `-Install` 을 주면
`%LOCALAPPDATA%\vman\bin` 에 복사한 뒤 `vman setup` 까지 실행합니다.

> `이 시스템에서 스크립트를 실행할 수 없으므로...` 오류는 PowerShell 실행 정책 때문입니다.
> 위 명령의 `-ExecutionPolicy Bypass` 가 시스템 설정을 바꾸지 않고 이번 실행만 허용합니다.
>
> `dotnet` 을 못 찾으면 SDK 설치 직후 열려 있던 터미널이라 PATH가 낡은 것입니다. 새 터미널을 여세요.

### 리눅스 / WSL2

```bash
git clone https://github.com/<사용자명>/vman.git
cd vman
./build.sh --install
exec $SHELL -l          # 또는 새 터미널
```

`dist/vman` 이 생기고, `--install` 을 주면 `~/.local/share/vman/bin` 에 복사한 뒤
`vman setup` 까지 실행합니다. `setup` 은 `~/.bashrc` · `~/.zshrc` · `~/.profile` 중
있는 것에 두 줄짜리 블록을 넣고, 실제 내용은 vman 이 관리하는 `env.sh` 한 장에 둡니다.

SDK가 없다면 sudo 없이도 설치할 수 있습니다.

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

> **WSL2 와 윈도우는 서로 별개입니다.** 루트가 다르고(`~/.local/share/vman` vs
> `%LOCALAPPDATA%\vman`) 받아오는 배포본도 리눅스용 / 윈도우용으로 갈립니다.
> WSL 안에서는 리눅스 런타임을 쓰는 것이 맞습니다. 양쪽에서 쓰고 싶으면 양쪽에 각각 설치하세요.

## 사용법

```bash
vman setup                              # 최초 1회
vman doctor                             # PATH에서 왜 안 잡히는지 진단
vman env                                # 이 셸에 적용할 코드 출력 (eval 용)
vman reload                             # 이 창을 새 터미널과 같은 환경으로 다시 읽기

vman venv                               # 이 폴더에 가상환경 생성 + 활성화
vman activate                           # 이 폴더의 가상환경 적용
vman deactivate                         # 해제
vman menu install                       # 탐색기 우클릭 메뉴 등록 (윈도우)
vman autoactivate on|off                # 폴더 이동 시 자동 활성화

vman available python                   # 받을 수 있는 버전 조회
vman install python 3.12                # 접두어를 주면 최신 패치 (→ 3.12.14)
vman install java 21                    # Temurin JDK
vman install node 22.5.1
vman import python 3.11.9 /opt/python311   # 이미 설치된 것을 등록 (복사 없음)

vman list                               # * 가 현재 버전
vman use python 3.12.14
vman use java 21                        # 부분 일치 허용
vman current                            # 실제 실행해서 버전 확인
vman remove node 22.5.1
```

전체 명령은 `vman help` 를 보세요.

설치한 도구에 아직 지정된 버전이 없으면 `install` / `import` 가 **그 자리에서 바로
활성화**합니다. "분명 깔았는데 PATH에서 안 보인다"의 가장 흔한 원인이 `use` 를
잊는 것이라서 그렇습니다. 이미 쓰는 버전이 있으면 건드리지 않습니다.

## 한 창에서 연속으로

버전 관리자가 성가신 지점은 늘 같습니다. 바꿔 놓고 나서 창을 새로 열어야 한다는 것.
vman 은 그럴 일이 거의 없습니다.

| 하는 일 | 새 창이 필요한가 | 이유 |
|---|---|---|
| `vman use` / `unset` | **아니오** | PATH 문자열은 그대로고 링크만 바뀝니다 |
| 트레이에서 버전 전환 | **아니오** | 위와 같습니다. 열려 있는 모든 터미널에 즉시 |
| `vman install` / `import` | **아니오** | 설치 위치가 PATH에 이미 들어 있는 경로 아래입니다 |
| `vman setup` (최초 1회) | 자동 처리 | 아래 설명 |

`setup` 만은 PATH 자체를 늘리는 작업이라 사정이 다릅니다. 프로세스는 시작할 때 환경
블록을 **복사**해서 쓰기 때문에, 밖에서 이미 열린 셸의 환경을 바꿔 넣을 방법은 없습니다.

그래서 vman 은 셸 안에 `vman` **함수**를 심습니다. 함수는 셸 안에서 돌기 때문에
그 셸의 환경을 바꿀 수 있습니다. `setup` 을 끝내면 함수가 이어서 새 PATH를 그 창에
적용합니다.

```
$ vman setup --force
루트: /home/me/.local/share/vman
셸 설정에 vman 을 연결했습니다.
  수정: /home/me/.bashrc
  생성: /home/me/.local/share/vman/env.sh

이 창에도 방금 반영했습니다. 이어서 바로 쓰시면 됩니다.
```

### 이미 열린 창을 새로고침하기

`source ~/.zshrc` 에 해당하는 것이 필요하면 `vman reload` 입니다. 양쪽 OS에서 같습니다.

```
$ vman reload
이 창의 환경을 새로 읽었습니다.
  Python   3.11.9
  Java     temurin-17
  Node.js  22.5.1
```

이 창의 환경을 **지금 새 터미널을 열면 갖게 될 것**으로 통째로 갈아끼웁니다.
윈도우에서는 레지스트리(HKLM + HKCU)에서 다시 읽으므로 vman 이 아닌 다른 설치
프로그램이 PATH 를 바꿔 놓은 것까지 따라옵니다.

셸별로 원래 있는 수단과의 관계:

| | 리눅스 | 윈도우 PowerShell |
|---|---|---|
| 셸 설정 다시 읽기 | `source ~/.zshrc` | `. $PROFILE` |
| vman 만 다시 적용 | `eval "$(vman env)"` | `vman env \| Out-String \| Invoke-Expression` |
| 시스템에서 통째로 | `vman reload` | `vman reload` |

`. $PROFILE` 은 `source ~/.zshrc` 와 정확히 대응합니다. vman 이 프로필에 자기 블록을
심어 두므로 이것만으로도 vman 경로가 그 창에 적용됩니다.

다만 원리가 다르다는 점은 알아 두는 편이 좋습니다. **리눅스는 rc 파일이 PATH 의 원본**
이라 다시 읽으면 그만이지만, **윈도우의 원본은 레지스트리**이고 프로필은 아닙니다.
그래서 다른 설치 프로그램이 PATH 를 바꾼 경우 `. $PROFILE` 로는 안 따라옵니다.
그때가 `vman reload` 가 필요한 자리입니다.

> **윈도우 터미널의 새 탭**은 어떨까요. 1.16 부터 `compatibility.reloadEnvironmentVariables`
> 가 생겨서 새 탭은 환경변수를 다시 읽습니다(이 저장소는 1.24 빌드에 이 설정이 있는 것을
> 확인했습니다). 다만 **지금 보고 있는 탭은 어차피 바뀌지 않습니다.** 그 창을 고치는 건
> 위의 명령들뿐입니다.

### 맨 처음 한 번

함수가 아직 안 심긴 **맨 처음 한 번**만 직접 한 줄 실행하면 됩니다. `setup` 이 알려줍니다.

| 셸 | 명령 |
|---|---|
| bash · zsh | `eval "$(vman env)"` |
| fish | `vman env --shell fish \| source` |
| PowerShell | `vman env \| Out-String \| Invoke-Expression` |
| cmd | `for /f "delims=" %i in ('vman env --shell cmd') do @%i` |

`vman env` 는 최종 PATH를 계산해서 셸 문법으로 뱉기만 합니다. 직접 확인해 볼 수 있습니다.

```bash
$ vman env
export PATH='/home/me/.local/share/vman/bin:/home/me/.local/share/vman/current/python/bin:...'
export JAVA_HOME='/home/me/.local/share/vman/current/java'
```

`--revert` 를 주면 vman 경로를 걷어낸 PATH가 나옵니다. `vman unsetup` 도 같은 방식으로
그 창에서 즉시 되돌립니다.

셸 함수는 `use` 뒤에 `hash -r` 도 부릅니다. PATH가 그대로라도 셸이 예전에 잡아 둔
실행 파일 경로를 캐시하고 있으면 그것이 계속 이기기 때문입니다.

> 심기는 곳: 리눅스는 `~/.bashrc` · `~/.zshrc` · `~/.profile`,
> 윈도우는 PowerShell 프로필(`$PROFILE`). 둘 다 마커 사이 두 줄뿐이고,
> 실제 내용은 vman이 관리하는 `env.sh` / `env.ps1` 한 장에 있습니다.
> `vman unsetup` 이 정확히 걷어냅니다.

## 폴더별 pip 격리 — 가상환경

프로젝트마다 패키지를 따로 두려면 가상환경이 필요합니다. vman 이 만들어 줍니다.

```bash
cd ~/projects/myapp
vman venv                 # .venv 생성 + 이 창에서 바로 활성화
pip install requests      # 이 폴더에만 설치됩니다
```

`vman use python` 으로 지정해 둔 버전을 그대로 물려받습니다.
`.venv` 는 윈도우에서도 **실제로 숨겨집니다** — 점 접두어가 아니라 숨김 속성을 겁니다.

```bash
vman venv                 # .venv (기본, 숨김)
vman venv venv            # 이름 지정
vman activate             # 이 폴더(또는 상위)의 가상환경을 이 창에 적용
vman deactivate           # 해제
```

`activate` 는 상위 폴더까지 거슬러 올라가며 찾으므로 프로젝트 하위 어디에서 실행해도 됩니다.
`.venv` · `venv` · `env` 를 인식하고, 예전 버전이 만들던 `.pyenv` · `pyenv` 도 계속 인식합니다.

> **pyenv 를 쓰지 않는 이유.** `pyenv`(와 `pyenv-win`)는 파이썬 **버전** 관리자입니다.
> vman 이 하는 일과 같아서 둘을 같이 깔면 PATH 앞자리를 두고 다툽니다.
> 폴더별로 패키지를 가르는 것은 파이썬에 내장된 `venv` 모듈이 하는 일이라,
> vman 은 버전 전환만 맡고 격리는 venv 에 맡긴 뒤 둘을 이어 주기만 합니다.
>
> 폴더 이름은 `.venv` 가 기본입니다. VS Code 파이썬 확장과 PyCharm 이 작업 폴더의
> `.venv` 를 보고 인터프리터를 자동으로 잡아 주기 때문입니다. 다른 이름도 됩니다.

### 폴더를 옮기면 자동으로 (기본 켜짐)

프롬프트가 그려질 때마다 현재 폴더(와 그 위쪽)에 가상환경이 있는지 보고 알아서 켜고 끕니다.

```
~/projects           $ python -V     → 전역 (vman 이 지정한 버전)
~/projects/myapp     $ python -V     → myapp/.venv       ← 자동
~/projects/myapp/src $ python -V     → myapp/.venv       ← 위로 찾아 올라감
~/projects/other     $ python -V     → other/.venv       ← 알아서 전환
~/projects           $ python -V     → 전역              ← 알아서 해제
```

끄고 켜기:

```bash
vman autoactivate          # 현재 상태
vman autoactivate off
vman autoactivate on
```

트레이 메뉴의 **가상환경 자동 활성화** 로도 됩니다.

몇 가지 성질:

- **손으로 켠 것은 건드리지 않습니다.** `vman activate` 로 직접 켠 가상환경은 폴더를
  옮겨도 유지됩니다. 훅이 켠 것만 훅이 끕니다.
- 프롬프트마다 도는 코드지만 **폴더가 그대로면 즉시 빠져나옵니다.** 가상환경 탐색도
  셸 안에서 문자열 조작으로만 하고, vman 을 부르는 것은 대상이 실제로 바뀔 때뿐입니다.
- bash · zsh · PowerShell 에서 동작합니다. fish 는 아직 훅을 심지 않습니다.

> **VS Code 에서도 되나요?** 통합 터미널은 셸을 띄우므로 그대로 동작합니다.
> 터미널을 안 쓰더라도 VS Code 파이썬 확장이 작업 폴더의 `.venv` 를 스스로 찾아
> 인터프리터로 잡습니다 — 기본 이름을 `.venv` 로 둔 이유이기도 합니다.
> (편집기가 파일을 열 때 셸이 뜨는 것은 아니므로 훅과는 별개 경로입니다.)

### 탐색기 우클릭 (윈도우)

```powershell
vman menu install         # 등록
vman menu uninstall       # 해제
```

폴더를 우클릭하거나 폴더 안 빈 공간을 우클릭하면:

```
vman 가상환경 만들기  ▸  .venv  (숨김)
                         venv
```

콘솔 창이 번쩍이지 않도록 `vman-tray.exe` 가 처리하고 결과만 대화상자로 알립니다.

> 윈도우 11 은 이런 고전 메뉴 항목을 **「추가 옵션 표시」(Shift+F10)** 안쪽에 넣습니다.
> 새 상단 메뉴에 직접 올리려면 MSIX 로 패키징한 COM 핸들러가 필요해서 지원하지 않습니다.

## `vman doctor`

PATH는 먼저 걸리는 쪽이 이기는 구조입니다. vman 이 제 할 일을 다 해도 앞줄에 다른
무언가가 서 있으면 그냥 가려집니다. `doctor` 는 그 상태를 그대로 보여줍니다.

```
$ vman doctor
[ OK ] 플랫폼: WSL2 (리눅스)
[ OK ] PATH 에 vman 경로가 모두 등록되어 있습니다
[ OK ] 셸 설정이 연결되어 있습니다
       /home/me/.bashrc, /home/me/.profile
[ OK ] Python: 3.12.14
[주의] python3 이(가) vman 이 아닌 다른 곳에서 잡힙니다
       /home/me/.pyenv/shims/python3
    → vman setup 후 새 셸을 열어 vman 경로가 앞에 오게 하세요.
```

검사 항목:

- PATH에 vman 경로가 다 들어 있는가 (윈도우는 **레지스트리**를 읽습니다 —
  현재 프로세스의 PATH는 낡았을 수 있으므로 "새 터미널을 열면 어떻게 되는가"를 봅니다)
- 윈도우: **이 창이 `vman setup` 보다 먼저 열렸는가** — 레지스트리는 멀쩡한데 이 창에만
  반영이 안 된 상태. 윈도우에서 가장 흔한 실패이고, 레지스트리만 보면 안 보입니다
- 리눅스: `env.sh` 와 rc 파일 블록이 살아 있는가
- 도구별로 링크가 걸려 있고 그 안에 실행 파일이 실제로 있는가
- `python` / `java` / `node` 가 **실제로 어디서 잡히는가** — vman 이 아니면 누가 가리는지
- 윈도우: 앱 실행 별칭 스텁(`WindowsApps\python.exe`)이 vman 보다 앞에 있는가
- WSL2: 윈도우 PATH가 interop 으로 딸려 들어왔는가

`vman doctor --fix` 는 vman 경로를 PATH 맨 앞으로 되돌리고(= `vman setup --force`),
셸 연동이 되어 있으면 **이 창에도 그 자리에서 반영**합니다.
자동으로 고칠 수 있는 건 이것뿐이고, 나머지는 안내만 합니다.

안내 문구는 지금 상황에 맞는 것만 나옵니다. 셸에 vman 함수가 심겨 있으면
`vman reload` 라고 알려주고, 아직 없으면 곧바로 먹는 한 줄을 대신 알려줍니다.

```
[문제] 이 터미널은 vman 설정보다 먼저 열린 창입니다
    → 이 창을 그 자리에서 고치려면:  vman reload
    →     창을 새로 열어도 됩니다. 둘 다 결과는 같습니다.
```

## 트레이 앱 (윈도우 전용)

`vman-tray.exe` 는 알림 영역에 상주합니다. 아이콘을 클릭하면:

```
Python   3.12.14  ▸   설치됨
Java  temurin-17  ▸   ✓ 3.12.14
Node.js   미설정  ▸     3.11.9
모양      Apple   ▸   ─────────
설치 폴더 열기        지정 해제
윈도우 시작 시 실행   ─────────
트레이에 항상 표시    설치 가능  ▸  3.14 ▸ 3.14.7
종료                  폴더 열기     3.13    3.14.6
```

- **설치 가능** 에는 인터넷에서 받아온 버전 목록이 뜹니다 (Node 100여 개, Python 90여 개).
  클릭하면 다운로드 → SHA256 검증 → 설치 → 전환까지 한 번에 처리합니다.
- **모양** 에서 `Apple` / `One UI 8` 테마와 밝게 / 어둡게 / 시스템 설정 따름을 고를 수 있습니다.
  선택은 `settings.json` 에 저장됩니다.
- 트레이가 화면 오른쪽에 있으면 서브메뉴가 전부 왼쪽으로 펴집니다.
- **새 터미널 열기** 는 레지스트리에서 PATH를 다시 만들어 넘긴 터미널을 띄웁니다.
  트레이 자신의 환경이 낡았어도 새 창은 올바릅니다.

GUI에서 버전을 바꿔도 **열려 있는 터미널을 새로 열 필요가 없습니다.** PATH 문자열은
그대로고 정션만 바뀌기 때문입니다. 실제로 확인한 동작입니다.

```
[1] 전환 전, 열려 있는 PowerShell 에서   ->  I-AM-AAA
[2] 외부 프로세스(= 트레이)가 버전 전환
[3] 같은 창, reload 없이, PATH 그대로   ->  I-AM-BBB
```

`where.exe` 는 두 번 다 같은 경로를 가리킵니다. 경로는 그대로고 그 경로가 가리키는
곳만 바뀌기 때문입니다. 그래서 GUI 전환에는 알림도 리로드도 필요 없습니다.

**예외는 하나뿐입니다** — `vman setup` **보다 먼저 열린 창.** 그런 창에는 vman 경로가
애초에 없어서 전환해도 보이지 않습니다. 트레이는 자기 자신이 그런 상태인지 보고
(레지스트리에는 있는데 자기 환경에는 없는지) 알림 문구를 바꿉니다.

```
Python → 3.12.14
setup 이후에 연 창에는 바로 적용됩니다.
그 전에 열린 창은 vman reload 가 필요합니다.
```

GUI가 남의 프로세스 환경을 고칠 방법은 원래 없습니다. 그래서 그 경우의 답은
`vman reload` 이거나 **새 터미널 열기** 입니다.

트레이는 WinForms 라서 윈도우 전용입니다. 리눅스에서는 CLI 만 빌드됩니다.

### 트레이 아이콘을 항상 표시하기

윈도우 11 은 새 트레이 아이콘을 기본으로 `^` 숨김 영역에 넣습니다.
`HKCU\Control Panel\NotifyIconSettings\{키}\IsPromoted` 를 1 로 쓰는 방법이 알려져
있지만 **빌드에 따라 탐색기가 무시합니다** (26200 에서 동작하지 않는 것을 확인).
확실한 방법은 수동 설정입니다:

**설정 → 개인 설정 → 작업 표시줄 → 기타 시스템 트레이 아이콘 → `vman` 켜기**

트레이 메뉴의 "트레이에 항상 표시" 를 누르면 이 설정 페이지가 바로 열립니다.

## 폴더 구조

윈도우는 `%LOCALAPPDATA%\vman\`, 리눅스는 `~/.local/share/vman/`
(`$XDG_DATA_HOME` 이 있으면 그 아래). `VMAN_ROOT` 로 덮어쓸 수 있습니다.

```
<루트>/
├─ bin/              PATH에 등록 (vman, vman-tray.exe)
├─ current/          링크 — 이 경로가 PATH에 박히고 절대 바뀌지 않음
│  ├─ python/  java/  node/
├─ versions/         실제 설치본
├─ downloads/        다운로드 임시 파일 + 버전 목록 캐시
├─ backup/           PATH / rc 파일 수정 전 백업
├─ env.sh            리눅스 — rc 파일이 읽어들이는 환경설정 + vman 셸 함수
├─ env.ps1           윈도우 — PowerShell 프로필이 읽어들이는 것 (같은 역할)
└─ settings.json     테마 설정
```

PATH에 들어가는 항목은 설치 시 한 번만 추가됩니다.

| | 윈도우 | 리눅스 |
|---|---|---|
| vman 자신 | `<루트>\bin` | `<루트>/bin` |
| Python | `current\python`, `current\python\Scripts` | `current/python/bin` |
| Java | `current\java\bin` | `current/java/bin` |
| Node | `current\node` | `current/node/bin` |

배포본 레이아웃이 OS마다 달라서 하위 경로가 갈립니다. 윈도우 Python 은 루트에
`python.exe` 가 있고 리눅스는 `bin/python3` 만 있습니다.

`JAVA_HOME` 은 `current/java` 로 고정되므로 버전을 바꿔도 값이 변하지 않습니다.

## 알려진 제약

- 파이썬 **버전** 전환은 전역입니다. 폴더별 자동 전환은 심(shim) 방식이 필요합니다.
  다만 **패키지**는 `vman venv` 로 폴더별로 가를 수 있습니다.
- 가상환경 자동 활성화는 bash · zsh · PowerShell 만 지원합니다. fish 는 `vman activate` 를 쓰세요.
- 윈도우: 서명되지 않은 exe이므로 SmartScreen 경고가 뜰 수 있습니다.
- 윈도우: `JAVA_HOME` 은 레지스트리 값이라 이미 열린 터미널에는 반영되지 않습니다.
  (값 자체는 고정이라 버전 전환으로는 바뀌지 않습니다.)
- 리눅스: glibc 배포판만 지원합니다. Alpine 같은 musl 계열은 받아오는 CPython 배포본이
  맞지 않습니다.
- `setup` 을 처음 한 창에서는 `eval "$(vman env)"` 한 줄이 필요합니다. 그 다음부터는
  셸에 심긴 `vman` 함수가 알아서 처리합니다.
- cmd.exe 는 셸 함수가 없어 자동 적용이 안 됩니다. `vman env --shell cmd` 를 쓰세요.
- x64 / arm64 만 지원합니다.

## 안전장치

- PATH 수정 전 `backup/user-path-{타임스탬프}.txt`,
  rc 파일 수정 전 `backup/{파일명}-{타임스탬프}.bak` 에 원본을 저장합니다.
- 윈도우: `REG_EXPAND_SZ` 타입을 보존해서 씁니다. `%USERPROFILE%` 같은 변수가 굳지 않습니다.
- 링크를 지우기 전에 실제 폴더인지 검사합니다. 실제 폴더면 거부하므로
  런타임 설치본이 실수로 지워지지 않습니다.
- `import` 로 등록한 버전을 `remove` 하면 링크만 끊고 원본은 남깁니다.
- Python 다운로드는 SHA256 을 검증합니다.
- 압축을 풀 때 대상 폴더 밖을 가리키는 항목과 링크를 거부합니다 (tar-slip 방어).

## 더 읽을거리

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 어떻게 동작하는지, 왜 이렇게 만들었는지
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — 문제 해결
- [docs/DEVELOPING.md](docs/DEVELOPING.md) — 빌드, 테마 미리보기 하네스

## 라이선스

MIT. 런타임은 재배포하지 않고 공식 배포처에서 직접 내려받습니다.

| 도구 | 배포처 | 라이선스 |
|---|---|---|
| Node.js | [nodejs.org/dist](https://nodejs.org/dist) | MIT |
| Java | [Adoptium Temurin](https://adoptium.net) | GPLv2 + Classpath Exception |
| Python | [python-build-standalone](https://github.com/astral-sh/python-build-standalone) | PSF |
