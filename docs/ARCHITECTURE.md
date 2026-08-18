# 구조

## 1. 버전 전환이란 결국 PATH 조작이다

`python`, `java`, `node` 명령이 어느 실행 파일로 가느냐는 전적으로 `PATH` 앞쪽에
무엇이 있느냐로 결정됩니다. 버전 관리자는 보통 셋 중 하나를 씁니다.

| 방식 | 원리 | 사례 | 문제 |
|---|---|---|---|
| PATH 재작성 | 전환할 때마다 PATH 문자열을 다시 씀 | 초보 스크립트 | 레지스트리 PATH가 깨지기 쉬움 |
| 정션 교체 | PATH엔 고정 경로 하나, 그 폴더가 실제 버전을 가리킴 | nvm-windows | 프로젝트별 자동 전환 불가 |
| 심(shim) | 가짜 exe가 설정을 읽고 진짜로 넘김 | pyenv-win, Volta | 구현 복잡, 프로세스 한 단계 추가 |

vman 은 **정션** 방식입니다. 전역 전환만 필요하다면 가장 단순하고 빠릅니다.

## 2. 전환의 실체

```
PATH: %LOCALAPPDATA%\vman\current\python      ← 설치 시 한 번 등록, 이후 불변
                            │
                            │  정션 (reparse point)
                            ▼
      %LOCALAPPDATA%\vman\versions\python\3.12.14
                            ▲
                            └── vman use python 3.11.9 →  versions\python\3.11.9
```

전환은 **정션을 지우고 다시 만드는 것**이 전부입니다 ([`Junction.Repoint`](../src/VMan.Core/Junction.cs)).
밀리초 단위로 끝나고 PATH 문자열은 손대지 않습니다.

핵심은 **디렉터리 정션은 일반 사용자 권한으로 만들 수 있다**는 점입니다.
심볼릭 링크는 관리자 권한이나 개발자 모드가 필요하지만 정션은 아닙니다.
그래서 vman 은 관리자 권한 없이 설치됩니다.

`Junction.cs` 는 `CreateFile` + `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` 를 P/Invoke 로
호출하고 `REPARSE_DATA_BUFFER` 바이트 레이아웃을 직접 조립합니다. 대상 경로 앞에는
NT 네임스페이스 접두어 `\??\` 가 붙어야 합니다.

## 3. PATH 를 다루는 규칙

`Environment.SetEnvironmentVariable(..., User)` 를 **쓰지 않습니다.** 두 가지 이유가 있습니다.

1. `REG_EXPAND_SZ` 를 `REG_SZ` 로 바꿔버립니다. 그러면 `%USERPROFILE%` 같은 변수가
   전개된 문자열로 굳어버립니다.
2. 구버전에서 1024자 절단 버그가 있습니다. 사용자 PATH가 통째로 날아가는 유명한 사고입니다.

대신 [`EnvManager`](../src/VMan.Core/EnvManager.cs) 가 `HKCU\Environment` 를 직접 다룹니다.

- 읽을 때 `RegistryValueOptions.DoNotExpandEnvironmentNames` 로 원본 문자열을 보존
- `GetValueKind` 로 원래 타입을 확인해 같은 타입으로 되돌려 씀
- **쓰기 전에 항상** `backup\user-path-{타임스탬프}.txt` 에 원본을 저장

애초에 정션 방식이라 PATH는 `vman setup` 때 한 번만 수정하므로 노출 자체가 최소입니다.

## 4. 이미 열린 터미널이 갱신되지 않는 이유

프로세스는 시작할 때 환경 블록을 **복사**해서 씁니다. 레지스트리를 바꿔도 이미 떠 있는
`cmd` / PowerShell 창은 자기 사본을 계속 봅니다. `WM_SETTINGCHANGE` 브로드캐스트
(`EnvManager.Broadcast`)는 탐색기와 앞으로 새로 뜨는 프로세스에만 영향을 줍니다.

정션 방식은 PATH 문자열이 안 바뀌므로 이 문제를 대부분 우회하지만,
`JAVA_HOME` 같은 별도 변수는 영향을 받습니다. 그래서 `JAVA_HOME` 을 특정 버전이 아니라
`current\java`(정션 자신)로 고정해 두었습니다. 전환해도 값이 변하지 않습니다.

## 5. 프로젝트 구성

```
src/
├─ VMan.Core/     로직 전부. CLI와 트레이가 공유한다
│  ├─ Layout.cs           도구 정의(ToolDef)와 모든 경로
│  ├─ Junction.cs         정션 생성/조회/삭제 P/Invoke
│  ├─ EnvManager.cs       HKCU\Environment 직접 조작 + 백업
│  ├─ VersionManager.cs   설치본 목록, 전환, import, 삭제
│  ├─ Downloader.cs       공식 배포처에서 받아 압축 해제
│  ├─ VersionCatalog.cs   설치 가능 버전 목록 (12시간 캐시)
│  └─ Settings.cs         테마 설정 저장
├─ VMan.Cli/      코어를 부르는 얇은 래퍼 → vman.exe
└─ VMan.Tray/     WinForms 상주 앱 → vman-tray.exe
   └─ Theming/    메뉴를 전부 직접 그린다
```

새 도구를 추가하려면 `Layout.cs` 의 `ToolDef` 에 항목을 하나 더하고
`Downloader` 에 받는 방법을 붙이면 됩니다. 나머지는 자동으로 따라옵니다.

## 6. 런타임을 어디서 받는가

재배포 라이선스 문제를 피하려고 **아무것도 번들하지 않고** 공식 배포처에서 직접 받습니다.

| 도구 | 출처 | 비고 |
|---|---|---|
| Node.js | `nodejs.org/dist/index.json` | 이 PC에 맞는 빌드가 있는 버전만 |
| Java | Adoptium API | Oracle JDK 는 배포 조건이 까다로워 피함 |
| Python | python-build-standalone | 공식 embeddable 은 pip 이 없어 실사용 불가 |

Python 목록과 SHA256 은 `uv` 가 관리하는 메타데이터 인덱스에서 가져옵니다.

### tar 파싱에 관한 주의

python-build-standalone 아카이브는 100바이트 `name` 필드를 재사용하면서 NUL 뒤를
0으로 지우지 않습니다. POSIX 상 리더는 첫 NUL 에서 멈춰야 하고 `bsdtar` 는 그렇게 하지만,
.NET 의 `TarReader` 는 잔여 바이트까지 이름에 포함시켜 `python.exe` 가
`python.exe_hon.exe` 로 풀립니다.

그래서 `TarFile.ExtractToDirectory` 를 쓰지 않고 엔트리를 직접 순회하며 이름을 첫 NUL 에서
자릅니다 (`Downloader.ExtractTarAsync`). 겸사겸사 경로 탈출(tar-slip) 방어도 넣었습니다.

## 7. 트레이 메뉴를 직접 그리는 이유

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
