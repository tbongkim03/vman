# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/) 를 따르고,
버전은 [유의적 버전](https://semver.org/lang/ko/) 을 따릅니다.

## [미출시]

### 추가

- **가상환경이 켜졌는지 눈에 보입니다.** 켜져 있으면 프롬프트 앞에 `(.venv)` 가 붙습니다.
  표준 `activate` 스크립트가 하는 일과 같은데, vman 은 그 스크립트를 부르지 않고
  PATH 만 조작해서 그동안 아무 표시가 없었습니다.
  - 원본 프롬프트를 한 번만 보관해 두고 매번 그것에서 다시 만듭니다. 앞에 덧붙이기만
    하면 가상환경을 옮겨 다닐 때 `(a) (b) (c)` 처럼 접두어가 쌓입니다.
  - `VMAN_VENV_PROMPT=0` 으로 표시만 끌 수 있습니다. 가상환경 자체는 그대로입니다.
  - 활성화할 때 `VIRTUAL_ENV_PROMPT` 도 심습니다. 파이썬 3.11 부터 표준 venv 가 쓰는
    변수라 oh-my-posh · starship 같은 셸 테마가 이것을 읽어 자기 방식으로 표시합니다.
- `vman current` 가 활성 가상환경과 그 파이썬 버전을 보여줍니다. 꺼져 있는데 이 폴더에
  가상환경이 있으면 그것도 알려줍니다. 프롬프트를 못 보는 자리(스크립트, cmd,
  편집기 터미널)를 위한 것입니다.

### 수정

- 이미 가상환경이 있는 폴더에서 `vman venv` 가 "이미 가상환경이 있습니다" 로 실패하고
  끝났습니다. 켜는 방법을 따로 찾아야 하는 막다른 길이었습니다. 이제 있으면 그것을
  그대로 쓰고 `vman activate` 를 안내합니다. 탐색기 우클릭도 마찬가지입니다.
  (같은 이름의 폴더가 가상환경이 아니면 그대로 실패합니다. 사람이 보고 정할 일입니다.)
- 활성화 안내가 `vman env --activate | Out-String | Invoke-Expression` 이라는 eval 주문을
  먼저 보여줬습니다. 사람이 칠 명령은 `vman activate` 이므로 그것을 앞세우고,
  eval 주문은 셸 연동이 아직 안 된 경우의 대비책으로 뒤에 둡니다.
- **윈도우에서 `vman doctor` 가 셸 연동을 아예 검사하지 않았습니다.**
  `if (Platform.IsWindows) return;` 으로 통째로 건너뛰고 있었습니다. 그래서
  `vman activate` 가 이 창에 안 먹는 상태에서도 doctor 는 "다 정상" 이라고 답했습니다.
  이제 `env.ps1` 과 `$PROFILE` 블록을 확인하고, 설정은 되어 있는데 이 창이 그보다
  먼저 열린 경우도 구분해서 알려줍니다.
- 셸 연동이 없을 때 `vman activate` · `vman reload` · `vman setup` 이 eval 주문
  (`vman env ... | Out-String | Invoke-Expression`)만 던지고 끝났습니다. 그것은 우회책인데
  마치 사용법인 것처럼 보여서, 매번 그 줄을 쳐야 하는 줄 알게 됩니다.
  이제 세 자리 모두 원인을 말하고 상황에 맞는 해결책을 앞세웁니다.
  - 연동이 아예 없음 → `vman setup` 후 새 터미널
  - 창이 연동보다 먼저 열림 → 새 터미널, 또는 이 창을 그대로 쓰려면 `. $PROFILE`
  - eval 주문은 최후의 수단으로 맨 뒤에 둡니다.

  두 상황은 해결책이 다른데 그동안 같은 문구를 보여주고 있었습니다.

## [0.2.0] - 2026-08-19

윈도우 전용에서 벗어나고, 폴더별 가상환경과 진단기를 얹었습니다.

### 추가

**리눅스 / WSL2 지원**

- OS 별로 갈리는 지점을 파사드 둘로 묶었습니다. `Links` 가 정션(윈도우)과 심볼릭
  링크(리눅스)를, `EnvStore` 가 `HKCU\Environment` 와 rc 파일 + `env.sh` 를 감춥니다.
  `VMan.Core` 의 TFM 을 `net8.0-windows` → `net8.0` 으로 내리고, 윈도우 전용 코드는
  `Platform.IsWindows` (`SupportedOSPlatformGuard`) 로 감쌉니다.
- `build.sh` 추가. 트레이는 WinForms 라 윈도우 전용이므로 CLI 만 만듭니다.
- tar 의 심볼릭 링크와 실행 권한을 살립니다. `bin/python3` → `python3.12` 같은
  링크를 잃으면 리눅스 런타임이 아예 돌지 않습니다.
- 아카이브 형식을 URL 확장자가 아니라 앞 2바이트로 판단합니다. Adoptium 의 다운로드
  URL 에는 확장자가 없고 리다이렉트 뒤에야 zip / tar.gz 가 갈립니다.

**한 창에서 연속으로 쓰기**

- `vman env` / `vman reload` — 프로세스는 부모 셸의 환경을 바꿀 수 없으므로 셸 안에
  `vman` 함수를 심습니다. PATH 계산은 C# (`ShellCode`) 에서 하고 셸에는 대입문 한 줄만
  내보내므로 sh · fish · PowerShell · cmd 를 같은 코드로 지원합니다.
- `vman doctor [--fix]` — PATH 를 앞에서부터 훑어 각 명령이 실제로 어디서 잡히는지
  계산하고, vman 이 아니면 누가 가리는지 보여줍니다. 윈도우에서는 레지스트리(새 터미널이
  갖게 될 것)와 현재 프로세스 환경(지금 이 창)을 나눠 봅니다. `setup` 보다 먼저 열린
  창에서 앱 실행 별칭 스텁이 잡히는 경우를 이 구분 없이는 찾을 수 없습니다.
- `vman setup --force` 로 vman 경로를 PATH 맨 앞으로 되돌립니다.
- `install` / `import` 는 그 도구에 지정된 버전이 없으면 바로 활성화합니다.

**폴더별 가상환경**

- `vman venv [이름]` — 현재 vman 이 가리키는 파이썬으로 `python -m venv` 를 부릅니다.
  기본 이름은 `.venv` 이고, 점으로 시작하면 윈도우에서 숨김 속성을 겁니다.
  `.pyenv` / `pyenv` / `venv` / `env` 도 계속 인식합니다.
- `vman activate` / `vman deactivate` — venv 가 딸려 주는 activate 스크립트를 부르지
  않고 `ShellCode` 로 직접 PATH 를 조작합니다. 이전에 켠 가상환경의 경로를 먼저
  걷어내므로 프로젝트를 오가도 PATH 가 쌓이지 않습니다.
- `vman autoactivate [on|off]` — 폴더를 옮기면 자동으로 켜고 끕니다(기본 켜짐).
  프롬프트마다 도는 코드라 세 단계로 걸러냅니다: 스위치 확인 → 직전 디렉터리와 비교 →
  셸 안 문자열 조작만으로 상위 탐색. 프로세스를 띄우는 것은 대상이 실제로 바뀔 때뿐입니다.
  손으로 켠 가상환경은 건드리지 않습니다.
- `vman menu install|uninstall|status` — 탐색기 우클릭 메뉴 (윈도우, HKCU 만 사용).
  `Directory\shell` 은 `%1`, `Directory\Background\shell` 은 `%V` 로 인자가 달라 따로
  등록합니다. 실행은 `vman-tray.exe --venv` 에 맡깁니다(콘솔 창이 번쩍이지 않도록).

**윈도우 11 상단 컨텍스트 메뉴**

- `src/VMan.ShellExt` 의 C++ `IExplorerCommand` 구현과 `packaging/` 의 MSIX 패키징.
  `packaging/build-msix.ps1` 로 컴파일 → 패키지 → 서명까지 **관리자 권한 없이** 됩니다.
  사이드로드만 인증서를 `LocalMachine\TrustedPeople` 에 넣어야 해서 관리자 권한이 필요합니다.
- 윈도우 11 (빌드 26100) 실기에서 확인했습니다. 폴더 아이콘 우클릭과 폴더 안 빈 공간
  우클릭 양쪽에서 메뉴가 상단에 뜨고, 하위 항목을 누르면 가상환경이 만들어집니다.
- 마커 파일(`C:\Users\Public\vman-shellext.on`)이 있을 때만 도는 진단 로그를 넣었습니다.
  대리자 안에서는 디버거를 붙이기 어렵고, `GetState` 가 `ECS_HIDDEN` 을 돌려주면
  메뉴가 오류 없이 조용히 사라지기 때문입니다.

### 수정

- `vman env` 의 `--shell` 값이 위치 인자로 잡혀 가상환경 이름으로 해석되던 문제.
  (`env --shell posix --activate` 가 "posix" 라는 이름을 찾다 실패)
- `vman help` 에서 `unsetup` 과 `where` 가 "가상환경" 섹션에 잘못 들어가 있던 문제.
- 셸 확장이 컴파일되지 않던 문제 네 가지. 첫 빌드에서 드러났습니다.
  - `cl` 에 `/utf-8` 이 없어 MSVC 가 BOM 없는 소스를 코드페이지 949 로 읽었습니다.
    한글 문자열 리터럴이 깨져 `C2001` 로 멈췄습니다.
  - `build-msix.ps1` 이 BOM 없는 UTF-8 이라 PowerShell 5.1 이 한글 출력을 깨뜨렸습니다.
  - 매니페스트가 `desktop5:Extension` 을 썼습니다. `FileExplorerContextMenus` 는
    **desktop4** 에만 정의되어 있습니다. 반대로 `ItemType` 은 desktop4 의 `Type` 이
    `*` 나 `.확장자`만 받으므로 `Directory` 를 쓰려면 **desktop5** 여야 합니다.
  - 매니페스트에 `windows.externalLocation` 확장을 넣었는데 그런 카테고리는 스키마에
    없습니다. 외부 경로는 `Add-AppxPackage -ExternalLocation` 으로 넘깁니다.
- 설치까지 성공한 뒤에도 메뉴가 뜨지 않던 문제. `VmanBinPath` 가
  `SHGetKnownFolderPath(FOLDERID_LocalAppData)` 를 플래그 없이 불렀는데, 셸 확장이 도는
  패키지 COM 대리자 안에서는 이 폴더가 패키지 전용 위치로 리다이렉트됩니다. 거기에
  `vman-tray.exe` 가 없으니 `GetState` 가 `ECS_HIDDEN` 을 돌려주고 메뉴가 오류 없이
  사라졌습니다. `KF_FLAG_NO_PACKAGE_REDIRECTION` 을 넘겨 해결했습니다.
- `docs/DEVELOPING.md` 의 빌드 명령에 백스페이스 제어문자(0x08)가 박혀 있어
  `.\packaging\build-msix.ps1` 이 `.\packaginguild-msix.ps1` 로 보이던 문제.
- 트레이 전환 알림 문구를 사실에 맞게 고쳤습니다. 링크만 바뀌므로 이미 열린 터미널에도
  즉시 반영됩니다. `setup` 보다 먼저 열린 창일 때만 문구가 달라집니다.
- `unsetup` 이 vman 블록만 있던 rc / 프로필은 파일째 치웁니다.

## [0.1.0] - 2026-08-19

첫 공개 버전.

### 추가

**코어**

- 디렉터리 정션으로 버전을 전환합니다. 관리자 권한이 필요 없습니다.
  `CreateFile` + `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` 을 P/Invoke 로 호출하고
  `REPARSE_DATA_BUFFER` 를 직접 조립합니다.
- 사용자 PATH 는 `vman setup` 때 한 번만 씁니다. `HKCU\Environment` 를 직접 다뤄
  `REG_EXPAND_SZ` 타입을 보존하고, 쓰기 전에 항상 백업을 남깁니다.
- `JAVA_HOME` 을 `current\java` 정션 자신으로 고정해, 버전을 바꿔도 값이 변하지 않습니다.
- 런타임을 재배포하지 않고 공식 배포처에서 받습니다.
  - Node.js — `nodejs.org/dist`
  - Java — Adoptium Temurin
  - Python — python-build-standalone (공식 embeddable 은 pip 이 없어 제외)
- Python 다운로드는 SHA256 을 검증합니다.
- 설치 가능 버전 목록을 12시간 캐시합니다.

**CLI (`vman.exe`)**

- `setup` / `unsetup` / `where`
- `list` / `current` / `use` / `unset`
- `available` / `install` / `import` / `remove`

**트레이 앱 (`vman-tray.exe`)**

- 도구별 서브메뉴에서 설치된 버전을 골라 즉시 전환합니다.
- "설치 가능" 서브메뉴에 인터넷에서 받아온 버전 목록이 뜨고, 클릭하면
  다운로드 → 검증 → 설치 → 전환까지 한 번에 처리합니다.
- Apple / One UI 8 두 가지 테마, 밝게 / 어둡게 / 시스템 설정 따름.
  선택은 `settings.json` 에 저장됩니다.
- 메뉴를 전부 직접 그립니다. 기본 `ContextMenuStrip` 의 회색 이미지 여백,
  각진 선택 사각형, 낡은 테두리를 쓰지 않습니다.
- 단일 인스턴스 보장, 시작 프로그램 등록 토글.
- `--preview <폴더>` 로 테마별 메뉴를 캡처해 PNG 와 레이아웃 수치를 남깁니다.

### 해결한 문제

개발 중 실제로 부딪혀 고친 것들입니다. 자세한 배경은
[ARCHITECTURE.md](docs/ARCHITECTURE.md) 에 있습니다.

- **tar 엔트리 이름이 깨짐** — python-build-standalone 아카이브는 100바이트 `name`
  필드를 재사용하면서 NUL 뒤를 0으로 지우지 않습니다. `bsdtar` 는 첫 NUL 에서 멈추지만
  .NET 의 `TarReader` 는 잔여 바이트까지 포함시켜 `python.exe` 가
  `python.exe_hon.exe` 로 풀렸습니다. 엔트리를 직접 순회하며 이름을 자르고,
  경로 탈출(tar-slip) 방어를 넣었습니다.
- **메뉴 창이 항목보다 좁아짐** — `ToolStripDropDownMenu` 가 크기를 이미지/체크 여백
  전제로 계산하고 `Padding` 도 되돌려놓아, 창 158px 에 항목 208px 이 되어 오른쪽
  텍스트가 잘렸습니다. `GetPreferredSize` 와 `DefaultPadding` 을 대체했습니다.
- **서브메뉴 방향이 레벨마다 뒤집힘** — 트레이가 화면 오른쪽 끝일 때 각 레벨이 방향을
  독립적으로 판단해 지그재그로 겹치고 1차 메뉴까지 덮었습니다. 커서 위치를 보고
  모든 서브메뉴 방향을 한 번에 통일합니다.
- **모서리가 각져 보임** — 둥글게 칠한 위로 사각 그림자가 남았습니다. Apple 테마는
  DWM 에 맡기고, 큰 반지름을 쓰는 One UI 테마는 직접 잘라내되 그림자를 끕니다.
- **`build.ps1` 한글 깨짐** — BOM 없는 UTF-8 을 Windows PowerShell 5.1 이 ANSI 로
  읽었습니다. UTF-8 BOM 을 붙였습니다.

### 알려진 제약

- 이미 열려 있는 터미널에는 반영되지 않습니다. 프로세스는 시작 시점의 환경 블록을
  복사해서 쓰기 때문입니다. 새 터미널을 여세요.
- 전역 전환만 지원합니다. 프로젝트 폴더별 자동 전환은 심(shim) 방식이 필요합니다.
- 트레이 아이콘 상시 표시는 수동 설정이 필요합니다.
  `IsPromoted` 레지스트리 값은 빌드 26200 에서 탐색기가 무시합니다.
- 서명되지 않은 exe 이므로 SmartScreen 경고가 뜰 수 있습니다.
- x64 / arm64 윈도우만 지원합니다.

[0.2.0]: https://github.com/tbongkim03/vman/releases/tag/v0.2.0
[0.1.0]: https://github.com/tbongkim03/vman/releases/tag/v0.1.0
