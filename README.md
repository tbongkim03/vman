# vman

윈도우용 Python / Java / Node.js 버전 관리자. CLI와 트레이 메뉴 양쪽에서 전환할 수 있습니다.

- **관리자 권한 불필요** — 심볼릭 링크 대신 디렉터리 정션을 씁니다
- **사용자 PATH는 설치 시 한 번만** 수정하고 이후로는 건드리지 않습니다
- **런타임을 재배포하지 않고** 공식 배포처에서 직접 받습니다
- .NET 8, MIT 라이선스

```
%LOCALAPPDATA%\vman\current\python   ← PATH에 박히는 고정 경로 (정션)
                    │
                    └─→ versions\python\3.12.14   전환할 때 이 화살표만 바꿉니다
```

## 설치

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요합니다.

```powershell
git clone https://github.com/tbongkim03/vman.git
cd vman
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

`dist\` 에 `vman.exe`(CLI)와 `vman-tray.exe`(트레이)가 생기고, `-Install` 을 주면
`%LOCALAPPDATA%\vman\bin` 에 복사한 뒤 `vman setup` 까지 실행합니다.

> `이 시스템에서 스크립트를 실행할 수 없으므로...` 오류는 PowerShell 실행 정책 때문입니다.
> 위 명령의 `-ExecutionPolicy Bypass` 가 시스템 설정을 바꾸지 않고 이번 실행만 허용합니다.
>
> `dotnet` 을 못 찾으면 SDK 설치 직후 열려 있던 터미널이라 PATH가 낡은 것입니다. 새 터미널을 여세요.

## 사용법

```powershell
vman setup                              # 최초 1회

vman available python                   # 받을 수 있는 버전 조회
vman install python 3.12                # 접두어를 주면 최신 패치 (→ 3.12.14)
vman install java 21                    # Temurin JDK
vman install node 22.5.1
vman import python 3.11.9 "C:\Python311"   # 이미 설치된 것을 등록 (복사 없음)

vman list                               # * 가 현재 버전
vman use python 3.12.14
vman use java 21                        # 부분 일치 허용
vman current                            # 실제 실행해서 버전 확인
vman remove node 22.5.1
```

### 명령 전체

| 명령 | 하는 일 |
|---|---|
| `vman setup` | 폴더 생성 + 사용자 PATH 등록. 최초 1회 |
| `vman unsetup` | PATH / `JAVA_HOME` 에서 vman 항목 제거. 설치본은 남김 |
| `vman where` | 루트 경로와 PATH 등록 항목 확인 |
| `vman list [도구]` | 설치된 버전 목록. `*` 가 현재 버전 (별칭 `ls`) |
| `vman current` | 현재 버전 + 실제로 실행해서 얻은 버전 문자열 |
| `vman use <도구> <버전>` | 버전 전환. 부분 일치 허용 |
| `vman unset <도구>` | 지정 해제 (정션 제거) |
| `vman available <도구>` | 설치 가능한 버전 조회 |
| `vman install <도구> <버전>` | 다운로드 후 설치 |
| `vman import <도구> <이름> <경로>` | 이미 설치된 런타임을 복사 없이 등록 |
| `vman remove <도구> <버전>` | 설치본 삭제 (별칭 `rm`). `import` 한 것은 링크만 끊음 |
| `vman help` | 도움말 |

도구 이름은 `python`, `java`, `node` 입니다.

환경변수 `VMAN_ROOT` 를 주면 루트를 바꿀 수 있습니다. 시험용으로 유용합니다
([DEVELOPING.md](docs/DEVELOPING.md#격리해서-시험하기) 참고).

## 트레이 앱

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

### 트레이 아이콘을 항상 표시하기

윈도우 11 은 새 트레이 아이콘을 기본으로 `^` 숨김 영역에 넣습니다.
`HKCU\Control Panel\NotifyIconSettings\{키}\IsPromoted` 를 1 로 쓰는 방법이 알려져
있지만 **빌드에 따라 탐색기가 무시합니다** (26200 에서 동작하지 않는 것을 확인).
확실한 방법은 수동 설정입니다:

**설정 → 개인 설정 → 작업 표시줄 → 기타 시스템 트레이 아이콘 → `vman` 켜기**

트레이 메뉴의 "트레이에 항상 표시" 를 누르면 이 설정 페이지가 바로 열립니다.

## 폴더 구조

```
%LOCALAPPDATA%\vman\
├─ bin\              PATH에 등록 (vman.exe, vman-tray.exe)
├─ current\          정션 — 이 경로가 PATH에 박히고 절대 바뀌지 않음
│  ├─ python\  java\  node\
├─ versions\         실제 설치본
├─ downloads\        다운로드 임시 파일 + 버전 목록 캐시
├─ backup\           PATH 수정 전 백업
└─ settings.json     테마 설정
```

PATH에 들어가는 항목은 다음 5개뿐이고, 설치 시 한 번만 추가됩니다.

```
%LOCALAPPDATA%\vman\bin
%LOCALAPPDATA%\vman\current\python
%LOCALAPPDATA%\vman\current\python\Scripts
%LOCALAPPDATA%\vman\current\java\bin
%LOCALAPPDATA%\vman\current\node
```

`JAVA_HOME` 은 `current\java` 로 고정 설정되므로 버전을 바꿔도 값이 변하지 않습니다.

## 알려진 제약

- **이미 열려 있는 터미널에는 반영되지 않습니다.** 프로세스는 시작 시점의 환경 블록을
  복사해서 쓰기 때문입니다. `WM_SETTINGCHANGE` 브로드캐스트는 탐색기와 새 프로세스에만
  영향을 줍니다. 새 터미널을 여세요.
- 전역 전환만 지원합니다. 프로젝트 폴더별 자동 전환은 심(shim) 방식이 필요합니다.
- 서명되지 않은 exe이므로 SmartScreen 경고가 뜰 수 있습니다.
- x64 / arm64 윈도우만 지원합니다.

## 안전장치

- PATH 수정 전 `backup\user-path-{타임스탬프}.txt` 에 원본을 저장합니다.
- `REG_EXPAND_SZ` 타입을 보존해서 씁니다. `%USERPROFILE%` 같은 변수가 굳지 않습니다.
- 정션을 지우기 전에 실제 폴더인지 검사합니다. 실제 폴더면 거부하므로
  런타임 설치본이 실수로 지워지지 않습니다.
- `import` 로 등록한 버전을 `remove` 하면 링크만 끊고 원본은 남깁니다.
- Python 다운로드는 SHA256 을 검증합니다.

## 더 읽을거리

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 어떻게 동작하는지, 왜 이렇게 만들었는지
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — 문제 해결
- [docs/DEVELOPING.md](docs/DEVELOPING.md) — 빌드, 테마 미리보기 하네스
- [CHANGELOG.md](CHANGELOG.md) — 버전별 변경 내역과 개발 중 해결한 문제들

## 라이선스

MIT. 런타임은 재배포하지 않고 공식 배포처에서 직접 내려받습니다.

| 도구 | 배포처 | 라이선스 |
|---|---|---|
| Node.js | [nodejs.org/dist](https://nodejs.org/dist) | MIT |
| Java | [Adoptium Temurin](https://adoptium.net) | GPLv2 + Classpath Exception |
| Python | [python-build-standalone](https://github.com/astral-sh/python-build-standalone) | PSF |
