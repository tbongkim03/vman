# 문제 해결

## 먼저 이것부터

```
vman doctor
```

PATH 에서 무엇이 무엇을 가리고 있는지 그대로 보여줍니다. 아래 항목 대부분은
`doctor` 가 이미 짚어주고 고치는 방법까지 알려줍니다.

## `python --version` 을 하면 `Python` 한 줄만 나오고 스토어 설치 안내가 뜹니다

**거의 항상 "그 터미널 창이 `vman setup` 보다 먼저 열렸기 때문"입니다.**

프로세스는 시작할 때 환경 블록을 **복사**해서 끝까지 씁니다. `vman setup` 이 레지스트리
PATH 를 고쳐도, 이미 열려 있던 PowerShell 창은 자기 사본을 계속 봅니다.
그 창에는 vman 경로가 아예 없으므로 `python` 은 PATH 를 계속 훑다가
`%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe` 에 걸립니다.

이 파일은 진짜 파이썬이 아니라 윈도우가 **기본으로 심어 두는 내용 없는 스텁**입니다.
실행하면 `Python` 한 줄을 찍고 스토어의 Python 설치 관리자를 권합니다.
vman 이 뭘 잘못한 것도, 파이썬이 안 깔린 것도 아닙니다. 그 창에서 안 보일 뿐입니다.

특히 `build.ps1 -Install` 을 실행한 **바로 그 창**에서 이 일이 납니다.
스크립트 안에서 `vman setup` 이 돌아 레지스트리는 갱신되지만, 그것을 띄운 바깥 창의
환경은 그대로이기 때문입니다.

```powershell
vman doctor      # "이 터미널은 vman 설정보다 먼저 열린 창입니다" 가 뜨면 이 경우입니다
```

**해결은 둘 중 아무거나.** 결과는 같습니다.

```powershell
vman reload      # 이 창을 그 자리에서 고침
```

또는 터미널을 새로 여세요. `doctor` 가 상황에 맞는 명령을 직접 알려줍니다.

새 창에서도 여전히 스토어 파이썬이 잡힌다면 그때는 순서 문제입니다.

```powershell
vman doctor          # "앱 실행 별칭이 vman 보다 PATH 앞에 있습니다" 인지 확인
vman setup --force   # vman 경로를 사용자 PATH 맨 앞으로 되돌림
```

스텁이 계속 거슬리면 아예 끌 수 있습니다.
**설정 → 앱 → 고급 앱 설정 → 앱 실행 별칭 → `python.exe` / `python3.exe` 끄기**

> 스토어에서 파이썬을 이미 받았더라도 vman 설치본이 사라지지는 않습니다.
> `vman list python` 으로 확인해 보세요. 새 창에서는 vman 이 지정한 버전이 이깁니다.

## 설치했는데 `vman list` 에는 있고 터미널에서는 안 잡힙니다

먼저 **새 터미널인지** 확인하세요 (바로 위 항목).

그 다음으로 흔한 것은 `vman use` 를 안 한 경우입니다. `install` / `import` 는 등록만 하고,
어느 버전을 쓸지는 `use` 가 정합니다.

```
vman list python        # * 표시가 없으면 지정된 버전이 없는 것
vman use python 3.11.9
```

(지금은 해당 도구에 지정된 버전이 하나도 없으면 `install` / `import` 가 그 자리에서
바로 활성화합니다. 이미 쓰는 버전이 있으면 건드리지 않습니다.)

그래도 안 잡히면 `vman doctor` 로 누가 가리고 있는지 확인하세요.
pyenv-win, nvm, conda, 시스템 JDK 처럼 PATH 앞줄을 차지하는 것들이 흔한 원인입니다.

## 버전을 바꿨는데 터미널에 반영되지 않습니다

`vman use` 는 링크만 바꾸고 PATH 문자열은 건드리지 않으므로 **이미 열린 터미널에도 바로
반영되는 것이 정상**입니다. 그런데도 옛날 버전이 나온다면:

- `vman setup` 을 방금 처음 한 상태라면 PATH 등록 자체가 아직 안 먹은 것입니다.
  윈도우는 새 터미널, 리눅스는 새 셸이나 `source ~/.bashrc` 가 필요합니다.
- 리눅스/맥 셸에서 `hash -r` 을 한 번 해보세요. bash 가 옛 경로를 캐시했을 수 있습니다.
  (vman 은 캐시되는 경로 자체가 고정이라 보통은 문제되지 않습니다.)
- `vman doctor` 로 다른 것이 앞에서 가로채는지 확인하세요.

지금 창에서만 급하게 PATH 를 얹으려면:

```powershell
# 윈도우
$env:PATH = "$env:LOCALAPPDATA\vman\current\python;$env:LOCALAPPDATA\vman\current\python\Scripts;$env:PATH"
```

```bash
# 리눅스 / WSL2
source ~/.local/share/vman/env.sh
```

## 창을 새로 열지 않고 지금 이 창을 고치고 싶습니다

```
vman reload
```

이 창의 환경을 "지금 새 터미널을 열면 갖게 될 것"으로 갈아끼웁니다.
`source ~/.zshrc` 와 같은 역할이고 윈도우에서도 똑같이 씁니다.

셸에 vman 함수가 아직 안 심긴 상태(첫 설치 전)라면 직접:

```bash
eval "$(vman env --reload)"                        # bash · zsh
```
```powershell
vman env --reload | Out-String | Invoke-Expression  # PowerShell
. $PROFILE                                          # 프로필만 다시 읽어도 됩니다
```

cmd.exe 는 셸 함수가 없어 자동 적용이 안 됩니다.

```cmd
for /f "delims=" %i in ('vman env --shell cmd --reload') do @%i
```

> 윈도우 터미널의 **새 탭**은 1.16 부터 환경변수를 다시 읽습니다
> (`compatibility.reloadEnvironmentVariables`). 하지만 **보고 있는 탭 자체는** 바뀌지
> 않습니다. 그 창을 고치려면 위 명령이 필요합니다.

## `이 시스템에서 스크립트를 실행할 수 없으므로...`

PowerShell 실행 정책 때문입니다. 시스템 설정을 바꾸지 않고 이번 실행만 허용합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

## `dotnet` 명령을 찾을 수 없습니다

**윈도우** — .NET SDK 설치 직후 열려 있던 터미널이라 PATH가 낡은 것입니다.
새 터미널을 여세요. 확인은 이렇게 합니다.

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" --list-sdks
```

**리눅스 / WSL2** — SDK 가 없는 것입니다. sudo 없이 홈 디렉터리에 설치할 수 있습니다.

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

`DOTNET_ROOT` 를 빼먹으면 빌드된 실행 파일이 `You must install .NET to run this
application` 을 뱉습니다. 다만 `build.sh` 로 만든 배포본은 자체 포함(self-contained)
이라 이 변수 없이도 돕니다.

## (WSL2) `vman setup` 을 했는데 `python3` 가 여전히 시스템 것입니다

새 셸을 열었는지 확인하세요. `setup` 은 rc 파일을 고칠 뿐이고, 이미 떠 있는 셸은
자기 PATH 사본을 계속 씁니다.

```bash
source ~/.local/share/vman/env.sh    # 지금 셸에만 적용
exec $SHELL -l                       # 또는 셸을 다시 시작
```

그래도 안 되면 `vman doctor` 를 보세요. WSL 안에서 흔한 원인 두 가지입니다.

- **pyenv / nvm / conda 가 앞줄에 있다.** 이들은 rc 파일 뒷부분에서 PATH 를 다시
  앞에 붙입니다. vman 블록이 rc 파일 **아래쪽**에 오도록 순서를 조정하거나,
  둘 중 하나만 쓰세요.
- **윈도우 PATH 가 딸려 들어왔다.** `/mnt/c/...` 아래 실행 파일이 먼저 잡히면
  리눅스 셸에서 윈도우 바이너리가 돕니다. 끄려면:

  ```ini
  # /etc/wsl.conf
  [interop]
  appendWindowsPath = false
  ```

  저장하고 `wsl --shutdown` 후 다시 여세요.

## (WSL2) 윈도우에 설치한 vman 이 WSL 에서 안 보입니다

정상입니다. 둘은 완전히 별개입니다.

| | 루트 | 받아오는 배포본 |
|---|---|---|
| 윈도우 | `%LOCALAPPDATA%\vman` | 윈도우용 (`.zip`, `python.exe`) |
| WSL2 | `~/.local/share/vman` | 리눅스용 (`.tar.gz`, `bin/python3`) |

WSL 안에서 윈도우용 `python.exe` 를 쓰는 것은 좋은 생각이 아닙니다 — 경로 구분자,
줄바꿈, 파일 권한이 전부 어긋납니다. WSL 에는 WSL 용으로 따로 설치하세요.

```bash
cd /mnt/c/경로/vman     # 윈도우 쪽 저장소를 그대로 써도 됩니다
./build.sh --install
```

빌드 산출물은 리눅스 홈(`~/.local/share/vman`)에 들어가므로 `/mnt/c` 의 느린
파일시스템이나 권한 문제에 걸리지 않습니다.

## 트레이 아이콘이 `^` 안에 숨어 있습니다

윈도우 11 은 새 트레이 아이콘을 기본으로 숨김 영역에 넣습니다.

**설정 → 개인 설정 → 작업 표시줄 → 기타 시스템 트레이 아이콘 → `vman` 켜기**

`IsPromoted` 레지스트리 값을 쓰는 방법이 알려져 있지만 빌드에 따라 탐색기가 무시합니다
(26200 에서 동작하지 않음). 위 설정이 확실합니다.

## 트레이 아이콘이 목록에 두 개 보입니다

빌드 폴더의 `dist\vman-tray.exe` 를 직접 실행한 적이 있으면 별도 항목으로 남습니다.
반응 없는 쪽을 지우려면:

```powershell
$keep = "$env:LOCALAPPDATA\vman\bin\vman-tray.exe"
Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath
    if ($p.ExecutablePath -like '*vman-tray.exe' -and $p.ExecutablePath -ne $keep) {
        Remove-Item $_.PSPath -Recurse -Force
    }
}
```

지운 뒤 탐색기를 재시작하면 목록이 정리됩니다.

## `링크가 아닌 실제 폴더입니다. 안전을 위해 삭제하지 않습니다`

`current/<도구>` 자리에 링크가 아닌 진짜 폴더가 있다는 뜻입니다. 의도적으로 만든 게
아니라면 내용을 확인하고 직접 지운 뒤 다시 `vman use` 를 실행하세요.
이 검사는 실수로 런타임 설치본이 지워지는 것을 막기 위한 것입니다.

## SHA256 이 일치하지 않습니다

다운로드가 중간에 끊겼거나 프록시가 내용을 바꾼 경우입니다. 다시 시도하세요.
계속 실패하면 회사 네트워크의 TLS 검사 프록시를 의심해볼 만합니다.

## PATH 를 되돌리고 싶습니다

```
vman unsetup
```

윈도우는 PATH 와 `JAVA_HOME` 에서 vman 항목을 제거하고, 리눅스는 rc 파일에서
vman 블록을 걷어내고 `env.sh` 를 지웁니다. 설치본은 `versions/` 에 남습니다.
수정 전 원본은 항상 백업되어 있습니다.

```powershell
Get-ChildItem "$env:LOCALAPPDATA\vman\backup"     # 윈도우
```

```bash
ls ~/.local/share/vman/backup                       # 리눅스 / WSL2
```

## SmartScreen 경고가 뜹니다

코드 서명 인증서 없이 빌드한 exe 라서 그렇습니다. 직접 빌드한 바이너리라면
"추가 정보 → 실행"으로 넘어가면 됩니다.

## 버전 목록이 비어 있거나 오래됐습니다

목록은 12시간(Python 인덱스는 24시간) 캐시됩니다. 강제로 갱신하려면 캐시 파일을 지우세요.

```powershell
Remove-Item "$env:LOCALAPPDATA\vman\downloads\catalog-*.json"
Remove-Item "$env:LOCALAPPDATA\vman\downloads\python-index.json"
```

```bash
rm ~/.local/share/vman/downloads/catalog-*.json
rm ~/.local/share/vman/downloads/python-index.json
```

## (리눅스) 설치한 Python 이 실행되지 않습니다

vman 이 받아오는 CPython 은 **glibc** 용 빌드입니다. Alpine 같은 musl 계열
배포판에서는 돌지 않습니다. 확인:

```bash
ldd --version        # musl 이라고 나오면 해당됩니다
```

`vman import` 로 배포판이 제공하는 파이썬을 등록해서 쓰는 편이 낫습니다.
