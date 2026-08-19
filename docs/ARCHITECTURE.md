# 구조

## 1. 버전 전환이란 결국 PATH 조작이다

`python`, `java`, `node` 명령이 어느 실행 파일로 가느냐는 전적으로 `PATH` 앞쪽에
무엇이 있느냐로 결정됩니다. 버전 관리자는 보통 셋 중 하나를 씁니다.

| 방식 | 원리 | 사례 | 문제 |
|---|---|---|---|
| PATH 재작성 | 전환할 때마다 PATH 문자열을 다시 씀 | 초보 스크립트 | PATH가 깨지기 쉬움 |
| 링크 교체 | PATH엔 고정 경로 하나, 그 폴더가 실제 버전을 가리킴 | nvm-windows, n | 프로젝트별 자동 전환 불가 |
| 심(shim) | 가짜 실행 파일이 설정을 읽고 진짜로 넘김 | pyenv, Volta | 구현 복잡, 프로세스 한 단계 추가 |

vman 은 **링크 교체** 방식입니다. 전역 전환만 필요하다면 가장 단순하고 빠릅니다.
윈도우에서는 정션, 리눅스에서는 심볼릭 링크를 쓰지만 원리는 같습니다.

## 2. 전환의 실체

```
PATH: <루트>/current/python                  ← 설치 시 한 번 등록, 이후 불변
                            │
                            │  링크 (윈도우=정션 / 리눅스=심볼릭 링크)
                            ▼
      <루트>/versions/python/3.12.14
                            ▲
                            └── vman use python 3.11.9 →  versions/python/3.11.9
```

전환은 **링크를 지우고 다시 만드는 것**이 전부입니다 ([`Links.Repoint`](../src/VMan.Core/Links.cs)).
밀리초 단위로 끝나고 PATH 문자열은 손대지 않습니다. 그래서 **이미 열려 있는 터미널에도
바로 반영됩니다** — 셸이 보는 경로 문자열은 그대로고, 그 경로가 가리키는 곳만 바뀝니다.

핵심은 **양쪽 다 관리자/root 권한 없이 만들 수 있다**는 점입니다.

| | 윈도우 | 리눅스 |
|---|---|---|
| 수단 | 디렉터리 정션 (mount point reparse point) | 심볼릭 링크 |
| 권한 | 불필요 | 불필요 |
| 구현 | `Junction.cs` (P/Invoke) | `Directory.CreateSymbolicLink` |

윈도우에서 심볼릭 링크를 쓰지 않는 이유가 이것입니다. 윈도우 심볼릭 링크는 관리자
권한이나 개발자 모드가 필요하지만 정션은 아닙니다. 리눅스에는 그런 제약이 없어서
심볼릭 링크를 그냥 씁니다.

`Junction.cs` 는 `CreateFile` + `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` 를 P/Invoke 로
호출하고 `REPARSE_DATA_BUFFER` 바이트 레이아웃을 직접 조립합니다. 대상 경로 앞에는
NT 네임스페이스 접두어 `\??\` 가 붙어야 합니다.

[`Links`](../src/VMan.Core/Links.cs) 가 이 둘을 하나의 인터페이스로 덮습니다.
`VersionManager` 이상의 코드는 어느 쪽인지 알 필요가 없습니다.

## 3. PATH 를 다루는 규칙

PATH 를 어디에 어떻게 심느냐는 OS마다 다릅니다.
[`EnvStore`](../src/VMan.Core/EnvStore.cs) 가 갈림길이고, 실제 구현은 두 벌입니다.

| | 윈도우 | 리눅스 / WSL2 |
|---|---|---|
| 저장소 | `HKCU\Environment` 레지스트리 | `~/.bashrc` · `~/.zshrc` · `~/.profile` |
| 구현 | [`EnvManager`](../src/VMan.Core/EnvManager.cs) | [`ShellEnv`](../src/VMan.Core/ShellEnv.cs) |
| 반영 시점 | 새 프로세스부터 | 새 셸부터 |
| 알림 | `WM_SETTINGCHANGE` 브로드캐스트 | 없음 (파일을 다시 읽을 뿐) |

### 윈도우 — 레지스트리를 직접 쓴다

`Environment.SetEnvironmentVariable(..., User)` 를 **쓰지 않습니다.** 두 가지 이유가 있습니다.

1. `REG_EXPAND_SZ` 를 `REG_SZ` 로 바꿔버립니다. 그러면 `%USERPROFILE%` 같은 변수가
   전개된 문자열로 굳어버립니다.
2. 구버전에서 1024자 절단 버그가 있습니다. 사용자 PATH가 통째로 날아가는 유명한 사고입니다.

대신 `EnvManager` 가 `HKCU\Environment` 를 직접 다룹니다.

- 읽을 때 `RegistryValueOptions.DoNotExpandEnvironmentNames` 로 원본 문자열을 보존
- `GetValueKind` 로 원래 타입을 확인해 같은 타입으로 되돌려 씀
- **쓰기 전에 항상** `backup\user-path-{타임스탬프}.txt` 에 원본을 저장

애초에 링크 방식이라 PATH는 `vman setup` 때 한 번만 수정하므로 노출 자체가 최소입니다.

### 리눅스 — rc 파일에 두 줄만 심는다

레지스트리 같은 중앙 저장소가 없으니 셸이 시작할 때 읽는 파일을 쓸 수밖에 없습니다.
다만 rc 파일을 직접 어지럽히지 않습니다. 실제 내용은 vman 이 관리하는 `env.sh` 한 장에
두고, rc 파일에는 그것을 읽어들이는 블록만 넣습니다.

```sh
# >>> vman >>>
[ -f "/home/me/.local/share/vman/env.sh" ] && . "/home/me/.local/share/vman/env.sh"
# <<< vman <<<
```

이렇게 하면 설정이 바뀌어도 `env.sh` 만 다시 쓰면 되고 rc 파일은 두 번 다시 건드리지
않습니다. `unsetup` 은 마커 사이를 잘라내면 끝이라 되돌리기도 정확합니다.
쓰기 전에는 `backup/{파일명}-{타임스탬프}.bak` 로 원본을 남깁니다.

`env.sh` 는 POSIX sh 문법만 씁니다. dash 가 읽는 `~/.profile` 에도 들어가기 때문입니다.
PATH 추가는 "이미 있으면 건너뛴다"라서 여러 번 읽혀도 PATH 가 부풀지 않습니다.

```sh
_vman_prepend() {
    case ":${PATH}:" in
        *":$1:"*) ;;
        *) PATH="$1${PATH:+:${PATH}}" ;;
    esac
}
```

`JAVA_HOME` 은 `[ -d ... ]` 로 링크 존재 여부를 보고 내보냅니다. 링크가 없는(= 지정
해제된) 상태에서 깨진 경로를 환경에 남기지 않으려는 것입니다. 값이 링크 경로로
고정이므로 버전을 바꿔도 `env.sh` 를 다시 쓸 일이 없습니다.

## 4. 이미 열린 터미널이 갱신되지 않는 이유

프로세스는 시작할 때 환경 블록을 **복사**해서 씁니다. 레지스트리를 바꿔도 이미 떠 있는
`cmd` / PowerShell 창은 자기 사본을 계속 봅니다. `WM_SETTINGCHANGE` 브로드캐스트
(`EnvManager.Broadcast`)는 탐색기와 앞으로 새로 뜨는 프로세스에만 영향을 줍니다.

링크 방식은 PATH 문자열이 안 바뀌므로 이 문제를 대부분 우회하지만,
`JAVA_HOME` 같은 별도 변수는 영향을 받습니다. 그래서 `JAVA_HOME` 을 특정 버전이 아니라
`current/java`(링크 자신)로 고정해 두었습니다. 전환해도 값이 변하지 않습니다.

리눅스도 사정은 같습니다. `env.sh` 를 이미 읽은 셸은 자기 PATH 사본을 계속 씁니다.
다만 여기서도 PATH 문자열이 고정이라 `vman use` 는 즉시 반영됩니다.
`setup` 직후에만 `source ~/.bashrc` 나 새 셸이 필요합니다.

> bash 는 실행 파일 경로를 해시로 캐시합니다(`hash -r` 로 지웁니다). vman 은
> 캐시된 경로 자체가 `current/python/bin/python3` 로 고정이라 이 캐시에 걸리지 않습니다.

## 5. 프로젝트 구성

```
src/
├─ VMan.Core/     로직 전부. CLI와 트레이가 공유한다. TFM 은 net8.0 (플랫폼 중립)
│  ├─ Platform.cs         OS 판별. 모든 분기가 여기를 거친다
│  ├─ Layout.cs           도구 정의(ToolDef)와 모든 경로. OS별로 갈린다
│  ├─ Links.cs        ┐   링크 파사드 — 위쪽 코드가 보는 유일한 창구
│  ├─ Junction.cs     ┘   └ 윈도우 구현 (정션 P/Invoke)
│  ├─ EnvStore.cs     ┐   환경 파사드 — 위쪽 코드가 보는 유일한 창구
│  ├─ EnvManager.cs   │   ├ 윈도우 구현 (HKCU\Environment + 백업)
│  ├─ ShellEnv.cs     ┘   └ 리눅스 구현 (rc 파일 + env.sh)
│  ├─ VersionManager.cs   설치본 목록, 전환, import, 삭제
│  ├─ Downloader.cs       공식 배포처에서 받아 압축 해제
│  ├─ VersionCatalog.cs   설치 가능 버전 목록 (12시간 캐시)
│  ├─ ShellCode.cs        셸별 대입문 생성 (vman env)
│  ├─ PowerShellEnv.cs    윈도우 PowerShell 프로필 연동
│  ├─ VenvManager.cs      폴더별 가상환경 생성/탐색
│  ├─ ExplorerMenu.cs     탐색기 우클릭 메뉴 (HKCU, 윈도우)
│  ├─ Doctor.cs           "왜 PATH 에서 안 잡히나" 진단
│  └─ Settings.cs         테마 설정 저장
├─ VMan.Cli/      코어를 부르는 얇은 래퍼 → vman / vman.exe   (net8.0)
└─ VMan.Tray/     WinForms 상주 앱 → vman-tray.exe            (net8.0-windows)
   └─ Theming/    메뉴를 전부 직접 그린다
```

### 플랫폼 분기를 다루는 방법

윈도우 전용 코드는 `[SupportedOSPlatform("windows")]` 로 표시하고
`if (Platform.IsWindows)` 로 감쌉니다. `Platform.IsWindows` 에는
`[SupportedOSPlatformGuard("windows")]` 가 붙어 있어서, 분석기가 그 안쪽을 윈도우
전용 구역으로 인정합니다. 이 속성이 없으면 레지스트리 호출마다 CA1416 경고가 뜹니다.

Core 의 TFM 이 `net8.0-windows` 가 아니라 `net8.0` 인 것도 같은 이유입니다.
레지스트리 API 는 `Microsoft.Win32.Registry` 패키지로 따로 끌어옵니다.
리눅스에서는 어셈블리에 실려 있기만 하고 호출되지 않습니다.

새 도구를 추가하려면 `Layout.cs` 의 `ToolDef` 에 항목을 하나 더하고
`Downloader` 에 받는 방법을 붙이면 됩니다. 나머지는 자동으로 따라옵니다.
`ToolDef` 는 윈도우용과 리눅스용 하위 경로/검사 파일을 각각 받습니다 —
배포본 레이아웃이 OS마다 다르기 때문입니다.

## 6. 런타임을 어디서 받는가

재배포 라이선스 문제를 피하려고 **아무것도 번들하지 않고** 공식 배포처에서 직접 받습니다.

| 도구 | 출처 | 비고 |
|---|---|---|
| Node.js | `nodejs.org/dist/index.json` | 이 PC에 맞는 빌드가 있는 버전만 |
| Java | Adoptium API | Oracle JDK 는 배포 조건이 까다로워 피함 |
| Python | python-build-standalone | 공식 embeddable 은 pip 이 없어 실사용 불가 |

Python 목록과 SHA256 은 `uv` 가 관리하는 메타데이터 인덱스에서 가져옵니다.

OS별로 갈리는 것은 URL 조각뿐입니다.

| | 윈도우 | 리눅스 |
|---|---|---|
| Node | `node-v22.5.1-win-x64.zip` | `node-v22.5.1-linux-x64.tar.gz` |
| Java | `.../ga/windows/x64/jdk/...` | `.../ga/linux/x64/jdk/...` |
| Python | 인덱스 키 `...-windows-x86_64-none` | 인덱스 키 `...-linux-x86_64-gnu` |

Python 인덱스 키의 꼬리표를 정확히 맞춰야 musl 빌드나 `x86_64_v3` 같은 최적화 변종이
섞여 들어오지 않습니다. Node 는 리눅스용 `.tar.xz` 도 있지만 .NET 에 xz 디코더가 없어
`.tar.gz` 를 받습니다.

### 아카이브 형식은 확장자로 판단할 수 없다

Adoptium 다운로드 URL 은 `.../jdk/hotspot/normal/eclipse` 로 끝납니다. 확장자가 없고,
리다이렉트를 따라가야 실체가 드러나며, 그 실체가 윈도우면 `.zip` 리눅스면 `.tar.gz` 입니다.
그래서 받은 파일의 **앞 2바이트를 보고** gzip(`1f 8b`)인지 판별합니다
(`Downloader.IsGzip`).

### tar 파싱에 관한 주의

python-build-standalone 아카이브는 100바이트 `name` 필드를 재사용하면서 NUL 뒤를
0으로 지우지 않습니다. POSIX 상 리더는 첫 NUL 에서 멈춰야 하고 `bsdtar` 는 그렇게 하지만,
.NET 의 `TarReader` 는 잔여 바이트까지 이름에 포함시켜 `python.exe` 가
`python.exe_hon.exe` 로 풀립니다.

그래서 `TarFile.ExtractToDirectory` 를 쓰지 않고 엔트리를 직접 순회하며 이름을 첫 NUL 에서
자릅니다 (`Downloader.ExtractTarAsync`). 겸사겸사 경로 탈출(tar-slip) 방어도 넣었습니다.

리눅스 배포본에는 이유가 하나 더 있습니다. **심볼릭 링크와 실행 권한 비트**입니다.

```
bin/python3  →  python3.12                        (상대 심볼릭 링크)
bin/npm      →  ../lib/node_modules/npm/bin/npm-cli.js
```

둘 중 하나라도 잃으면 런타임이 그냥 안 돕니다. 그래서 `TarEntryType.SymbolicLink` 를
실제 링크로 만들고(대상이 아직 안 풀렸을 수 있어 마지막에 몰아서), `TarEntry.Mode` 를
`File.SetUnixFileMode` 로 되살립니다. 링크는 **상대 경로 그대로** 심습니다. 그래야
설치 폴더를 옮겨도 링크가 유지됩니다. 윈도우에서는 둘 다 의미가 없어 건너뜁니다.

링크 대상이 압축 해제 폴더 밖을 가리키면 거부합니다. 파일 경로에 대한 tar-slip 방어와
같은 이유입니다.

## 7. PATH 는 먼저 걸리는 쪽이 이긴다 — `vman doctor`

vman 이 제 할 일을 다 해도 PATH 앞줄에 다른 무언가가 서 있으면 그냥 가려집니다.
이 실패는 조용해서, 사용자 눈에는 "설치했는데 안 보인다"로만 보입니다.
[`Doctor`](../src/VMan.Core/Doctor.cs) 는 그 상태를 그대로 드러냅니다.

핵심은 **PATH 를 앞에서부터 훑어 명령이 실제로 어디서 잡히는지 계산**하는 것입니다.
그 결과가 vman 경로가 아니면 누가 가리고 있는지 경로째로 보여줍니다.

윈도우에서는 현재 프로세스의 PATH 를 보지 않습니다. 그 값은 프로세스가 뜰 때 복사된
사본이라 낡았을 수 있습니다. 대신 `HKLM` + `HKCU` 를 읽어 **"지금 새 터미널을 열면
갖게 될 PATH"** 를 재구성합니다 (`EnvManager.EffectivePathEntries`).

### 가장 흔한 실패: 낡은 창

윈도우에서 압도적으로 흔한 원인은 **터미널이 `vman setup` 보다 먼저 열린 것**입니다.
레지스트리는 정확한데 그 창의 환경 블록만 옛것이라, 레지스트리만 검사하면 전부
[OK] 로 보입니다. 그래서 `Doctor` 는 두 PATH 를 **따로** 봅니다.

| | 무엇 | 어디서 |
|---|---|---|
| `EffectivePathEntries()` | 새 터미널을 열면 갖게 될 PATH | HKLM + HKCU 레지스트리 |
| `SessionPathEntries()` | 지금 이 창이 들고 있는 PATH | 현재 프로세스 환경변수 |

레지스트리에는 있는데 세션에는 없으면 → "이 터미널은 vman 설정보다 먼저 열린 창입니다".

이 상태에서 `python` 을 치면 vman 경로가 없는 것과 똑같이 동작합니다. PATH 를 계속
훑다가 `%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe` 에 걸리는데, 이 파일은
윈도우가 기본으로 심어 두는 **내용 없는 앱 실행 별칭 스텁**입니다. `Python` 한 줄을
찍고 스토어의 Python 설치 관리자를 권합니다.

여기서 사용자가 스토어 파이썬을 설치해도 아무것도 나아지지 않습니다. 그 창은 여전히
낡았고, 새 창에서는 어차피 vman 이 앞서기 때문입니다. 증상만 보고 "vman 이 설치를
실패했다"고 오해하기 딱 좋은 자리입니다.

`build.ps1 -Install` 을 실행한 바로 그 창이 정확히 이 함정에 빠집니다.
스크립트 안에서 `vman setup` 이 돌아 레지스트리는 갱신되지만, 그것을 띄운 바깥 창의
환경은 그대로입니다.

### 순서 문제: 앱 실행 별칭이 앞설 때

새 창인데도 스텁이 이긴다면 그때는 진짜 PATH 순서 문제입니다.
`Doctor` 는 스텁을 **내용 없는 파일 + WindowsApps 경로** 조합으로 식별하고,
PATH 상에서 vman 보다 앞에 있을 때만 보고합니다 (이 파일 자체는 윈도우에 늘 있으므로
존재만으로 경고하면 잡음이 됩니다).

대응은 `vman setup --force` — `PrependToUserPath(force: true)` 로 vman 경로를 사용자
PATH 맨 앞에 다시 붙입니다. `vman doctor --fix` 가 이것을 부릅니다.
스텁 자체는 설정 → 앱 → 고급 앱 설정 → 앱 실행 별칭 에서만 끌 수 있어 자동화하지 않습니다.

### WSL2

WSL 은 기본적으로 윈도우 PATH 를 물려받습니다(interop). `/mnt/c/...` 아래 실행 파일이
먼저 잡히면 리눅스 셸에서 윈도우 바이너리가 돌아가는 이상한 상태가 됩니다.
`Doctor` 는 이것을 알아보고 `/etc/wsl.conf` 의 `[interop] appendWindowsPath=false` 를
안내합니다.

WSL 안의 vman 과 윈도우의 vman 은 **루트가 달라서 서로 섞이지 않습니다**
(`~/.local/share/vman` vs `%LOCALAPPDATA%\vman`). 받아오는 배포본도 각자 자기 OS 것입니다.
양쪽에서 쓰려면 양쪽에 각각 설치하면 됩니다.

## 8. 한 창에서 연속으로 — 셸 함수

프로세스는 부모 셸의 환경을 바꿀 수 없습니다. 예외는 없습니다. 그래서 버전 관리자가
쓰는 방법은 예나 지금이나 하나뿐입니다 —
**셸이 실행할 코드를 문자열로 뱉고, 셸이 그것을 eval 한다.**

다만 vman 은 이것이 필요한 자리가 매우 좁습니다. 링크 교체 방식이라 PATH 문자열이
안 바뀌기 때문입니다.

| 하는 일 | PATH 문자열이 바뀌나 | 새 창이 필요한가 |
|---|---|---|
| `use` · `unset` · 트레이 전환 | 아니오 | 아니오 |
| `install` · `import` | 아니오 | 아니오 |
| `setup` · `unsetup` | **예** | 셸 함수가 처리 |

### `vman env` — 계산은 C#에서, 셸에는 대입문 한 줄

[`ShellCode`](../src/VMan.Core/ShellCode.cs) 는 최종 PATH를 **C#에서** 계산합니다.
셸 쪽으로는 대입문 한 줄만 나갑니다.

```
$ vman env
export PATH='/home/me/.local/share/vman/bin:...'
export JAVA_HOME='/home/me/.local/share/vman/current/java'
```

셸 스크립트에 로직을 넣지 않는 것이 요점입니다. 그 덕에 sh · fish · PowerShell · cmd
네 가지를 같은 코드로 지원하고, 셸별로 갈리는 것은 **인용 규칙과 대입문 문법뿐**입니다.

| 셸 | 문법 | 인용 |
|---|---|---|
| POSIX | `export PATH='...'` | `'` → `'\''` |
| fish | `set -gx PATH '...'` | `'` → `\'`, `\` → `\\` |
| PowerShell | `$env:PATH = '...'` | `'` → `''` |
| cmd | `set "PATH=..."` | (이스케이프 없음) |

### 셸 함수가 실제로 하는 일

`env.sh` / `env.ps1` 끝에 `vman` 을 감싸는 함수를 정의해 둡니다. 함수는 셸 안에서
돌기 때문에 그 셸의 환경을 바꿀 수 있습니다.

```sh
vman() {
    "$_vman_bin" "$@"
    case "$1" in
        setup)   eval "$("$_vman_bin" env --shell posix)" ;;
        unsetup) eval "$("$_vman_bin" env --shell posix --revert)" ;;
        use|unset|install|import|remove|rm) hash -r ;;
    esac
}
```

PowerShell 도 같은 구조입니다(`Invoke-Expression`). 두 셸 모두 함수가 외부 실행 파일보다
우선하므로 `vman` 을 치면 이쪽으로 들어옵니다.

함수는 `VMAN_SHELL` 도 세팅합니다. 덕분에 `ShellCode.Detect()` 가 추측할 필요 없이
어느 셸인지 알고, CLI 는 "함수를 통해 불렸다"는 사실도 알 수 있어
setup 뒤에 *"이 창에도 방금 반영했습니다"* 와 *"이 한 줄을 실행하세요"* 를 구분해 안내합니다.

### `vman reload` — `source ~/.zshrc` 의 대응물

`ShellCode.Reload` 는 `Apply` 와 출발점이 다릅니다.

| | 출발점 | 쓰임 |
|---|---|---|
| `Apply` | 이 창의 현재 PATH | vman 경로만 얹는다 |
| `Reload` | 시스템이 들고 있는 값 | 새 터미널과 같은 상태로 통째로 갈아끼운다 |

윈도우에서는 출발점이 레지스트리(HKLM + HKCU)입니다. 그래서 vman 이 아닌 다른 설치
프로그램이 PATH 를 바꿔 놓은 것까지 따라옵니다. 리눅스에는 그런 중앙 저장소가 없어
현재 PATH 가 곧 출발점이고, 결과적으로 `Apply` 와 같아집니다.

여기서 두 OS 의 구조 차이가 드러납니다. **리눅스는 rc 파일이 PATH 의 원본**이라
`source ~/.zshrc` 로 끝나지만, **윈도우의 원본은 레지스트리**이고 프로필은 아닙니다.
`. $PROFILE` 은 프로필이 하는 일만 다시 할 뿐입니다. vman 은 프로필에 자기 블록을
심으므로 그것만으로도 vman 경로는 복구되지만, 그게 전부는 아니라는 뜻입니다.

### `hash -r` 이 필요한 이유

`use` 는 PATH를 안 바꾸는데도 셸 캐시는 비워야 합니다. bash 는 한 번 찾은 실행 파일의
경로를 해시에 담아 두는데, vman 을 처음 쓰기 전에 `python3` 가 `/usr/bin/python3` 로
잡혀 있었다면 링크를 걸어도 그 캐시가 계속 이깁니다.
캐시된 경로가 `current/python/bin/python3` 로 바뀐 뒤로는 문제가 없습니다.

### 윈도우에서 레지스트리와 프로필을 둘 다 쓰는 이유

레지스트리만으로도 **새** 프로세스는 전부 챙겨집니다. GUI 앱이나 PowerShell 이 아닌
프로그램은 그쪽만 봅니다. 그래서 레지스트리 수정은 그대로 둡니다.

PowerShell 프로필([`PowerShellEnv`](../src/VMan.Core/PowerShellEnv.cs))을 더하는 이유는
"지금 이 창" 때문입니다. `vman setup` 을 실행한 그 창에서 곧바로 python 을 쳐 보는 것이
사람의 자연스러운 행동인데, 레지스트리만으로는 그 창이 절대 갱신되지 않습니다.
프로필 경로는 `Environment.GetFolderPath(MyDocuments)` 로 잡아서 OneDrive 로 리디렉션된
문서 폴더도 따라갑니다.

### GUI 는 어떻게 하나

트레이가 이미 떠 있는 터미널의 환경을 고칠 방법은 **없습니다.** 환경 블록은 프로세스
시작 시점에 복사되고 밖에서 건드릴 수 없습니다. 주입 같은 수단은 안정성과 보안 양쪽에서
받아들일 수 없습니다.

다행히 vman 에서는 이것이 거의 문제가 되지 않습니다. 트레이에서 버전을 바꿔도 정션만
바뀌므로 **열려 있는 모든 터미널에 즉시 반영**됩니다. 실측으로 확인했습니다 — 이미 떠
있는 PowerShell 에서 PATH 를 그대로 둔 채 외부 프로세스가 정션을 바꾸자, 같은 창의
다음 호출부터 새 대상이 잡혔습니다. `where.exe` 가 돌려주는 경로는 두 번 다 같습니다.

GUI 가 못 하는 일은 **`setup` 보다 먼저 열린 창** 하나뿐입니다. 그런 창에는 vman 경로가
애초에 없어서 전환해도 보이지 않습니다. 트레이는 남의 프로세스 환경을 볼 수 없지만
자기 자신은 볼 수 있어서, 레지스트리에는 있는데 자기 환경에 없으면 그때 같이 떠 있던
창들도 낡았다고 보고 알림 문구를 바꿉니다(`TrayApp.SessionIsStale`). 대응은 두 가지입니다.

1. `WM_SETTINGCHANGE` 브로드캐스트 — 탐색기와 앞으로 새로 뜨는 프로세스에 알립니다.
2. **새 터미널 열기** 메뉴 — 레지스트리에서 PATH를 재구성해 넘긴 터미널을 띄웁니다.
   트레이 자신의 환경이 낡았을 수 있으므로 그대로 물려주지 않는 것이 핵심입니다.
   그대로 물려주면 낡은 창을 하나 더 만드는 셈입니다.

## 9. 폴더별 격리는 venv 에 맡긴다

버전 관리자에게 흔히 따라오는 요구가 "프로젝트마다 패키지를 따로 두고 싶다"입니다.
여기서 pyenv 를 끌어오고 싶어지지만 그건 틀린 도구입니다.

| | 하는 일 | vman 과의 관계 |
|---|---|---|
| pyenv / pyenv-win | 파이썬 **버전** 전환 | 같은 일 — 같이 깔면 PATH 앞자리를 다툰다 |
| venv (표준 모듈) | 폴더별 **패키지** 격리 | 겹치지 않음 — 이쪽을 쓰면 된다 |

그래서 [`VenvManager`](../src/VMan.Core/VenvManager.cs) 는 새 메커니즘을 만들지 않고
현재 vman 이 가리키는 파이썬으로 `python -m venv` 를 부릅니다. `vman use python 3.12.14`
뒤에 만든 가상환경은 3.12.14 를 물려받고, `pyvenv.cfg` 의 `home` 이 `current/python`
(링크 자신)을 가리킵니다.

### 활성화는 이미 있는 장치를 재사용한다

venv 가 딸려 주는 `activate` 스크립트를 부르지 않습니다. 셸마다 `activate` ·
`activate.fish` · `Activate.ps1` 로 파일이 갈리는데, [8장](#8-한-창에서-연속으로--셸-함수)에서
만든 `ShellCode` 로 하면 어차피 대입문 몇 줄이면 끝나기 때문입니다.

```
$ vman env --activate
export PATH='/proj/.pyenv/bin:/home/me/.local/share/vman/bin:...'
export VIRTUAL_ENV='/proj/.pyenv'
unset PYTHONHOME
```

가상환경 bin 이 vman 경로보다 **앞**에 와야 `python` 과 `pip` 이 가상환경 것으로 잡힙니다.
이전에 활성화해 둔 가상환경의 bin 은 먼저 걷어내므로 여러 프로젝트를 오가도 PATH 가
쌓이지 않습니다. 셸 함수가 `venv` · `activate` · `deactivate` 를 가로채므로
사용자는 eval 을 신경 쓸 필요가 없습니다.

`Find` 는 상위 폴더로 거슬러 올라가며 `pyvenv.cfg` 가 있는 폴더를 찾습니다.
프로젝트 하위 어디에서 실행해도 잡히게 하려는 것입니다.

### 탐색기 우클릭 메뉴

[`ExplorerMenu`](../src/VMan.Core/ExplorerMenu.cs) 이 `HKCU` 아래에만 씁니다.
관리자 권한이 필요 없습니다. 두 곳에 따로 등록해야 합니다.

| 키 | 언제 | 탐색기가 넘기는 인자 |
|---|---|---|
| `Directory\shell` | 폴더 아이콘을 우클릭 | `%1` |
| `Directory\Background\shell` | 폴더 안 빈 공간을 우클릭 | `%V` |

하위 메뉴는 `MUIVerb` + `subcommands=""` 조합으로 만듭니다. `subcommands` 를 **빈 문자열**로
두어야 탐색기가 그 키 아래 `shell\` 하위키들을 펼칩니다. 값이 아예 없으면 하위 메뉴가
생기지 않습니다. 하위키 이름이 정렬 순서를 정하므로 번호를 붙입니다.

실행은 `vman.exe` 가 아니라 `vman-tray.exe --venv` 에 맡깁니다. 콘솔 앱을 물리면 검은 창이
번쩍이고 결과도 못 보여주기 때문입니다. 트레이는 WinExe 라 창이 없고, 이 모드에서는
트레이 아이콘을 만들지 않고 대화상자만 띄운 뒤 끝냅니다(아이콘이 두 개 되면 안 되므로).

> 윈도우 11 은 이런 고전 항목을 「추가 옵션 표시」 안쪽에 넣습니다. 새 상단 메뉴에
> 올리려면 MSIX 패키징 + `IExplorerCommand` COM 핸들러가 필요해 지원하지 않습니다.

## 10. 트레이 메뉴를 직접 그리는 이유

WinForms 기본 `ContextMenuStrip` 은 왼쪽 회색 이미지 여백, 각진 선택 사각형, 낡은 테두리를
그립니다. 색만 바꿔서는 "회색 윈도우 메뉴"를 벗어날 수 없어 그리기를 전부 대체했습니다.

- [`ThemedRenderer`](../src/VMan.Tray/Theming/ThemedRenderer.cs) — 배경, 선택, 텍스트, 구분선, 화살표, 체크
- [`VmanMenuItem`](../src/VMan.Tray/Theming/VmanMenuItem.cs) — "왼쪽 이름 + 오른쪽 보조 텍스트" 레이아웃 계산
- [`Theme`](../src/VMan.Tray/Theming/Theme.cs) — Apple / One UI 8 두 벌의 치수와 색

### 함정 1: 창이 항목보다 좁아진다

`ToolStripDropDownMenu` 는 자기 크기를 이미지/체크 여백이 있다는 전제로 계산하고
`Padding` 도 `{Left=8, Right=1}` 로 되돌려 놓습니다. `ShowImageMargin = false` 로는 막히지
않습니다. 그 결과 **창(158px)이 항목(208px)보다 좁아져** 오른쪽 텍스트가 잘립니다.

[`VmanDropDown`](../src/VMan.Tray/Theming/VmanDropDown.cs) 에서 `GetPreferredSize` 와
`DefaultPadding` 을 통째로 대체해 해결했습니다.

### 함정 2: 서브메뉴 방향이 레벨마다 뒤집힌다

트레이는 보통 화면 오른쪽 끝입니다. 방향을 기본값에 맡기면 각 레벨이 "오른쪽에 자리가
있나?"를 독립적으로 판단해서, 2차는 왼쪽 · 3차는 다시 오른쪽 · 4차는 또 왼쪽으로 튀며
서로 겹치고 1차까지 덮습니다.

메뉴가 열릴 때 커서 위치를 보고 (`TrayApp.ShouldDropLeft`) 오른쪽에 여유가 없으면
모든 서브메뉴의 `DropDownDirection` 을 `Left` 로 **한 번에 통일**합니다.

### 함정 3: 큰 모서리는 DWM 이 못 한다

`DWMWA_WINDOW_CORNER_PREFERENCE` 는 8px 정도까지만 지원합니다. Apple 테마(8px)는 DWM 에
맡겨 안티에일리어싱과 그림자를 깔끔하게 얻고, One UI 테마(20px)는 `Region` 으로 직접
잘라내되 사각 그림자를 끕니다. 둘을 같이 쓰면 둥글게 칠한 위로 사각 그림자가 남아
각져 보입니다.
