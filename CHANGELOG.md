# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/) 를 따르고,
버전은 [유의적 버전](https://semver.org/lang/ko/) 을 따릅니다.

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

[0.1.0]: https://github.com/tbongkim03/vman/releases/tag/v0.1.0
