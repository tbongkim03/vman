# 개발

## 빌드

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 가 필요합니다.

### 윈도우

```powershell
dotnet build VMan.sln -c Release          # 컴파일만 (빠름)
powershell -ExecutionPolicy Bypass -File .\build.ps1           # 단일 exe 게시 → dist\
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install  # 게시 + 설치 + setup
```

### 리눅스 / WSL2

```bash
dotnet build src/VMan.Cli/VMan.Cli.csproj -c Release   # 컴파일만
./build.sh                                             # 단일 파일 게시 → dist/vman
./build.sh --install                                   # 게시 + 설치 + setup
./build.sh --rid linux-arm64                           # 다른 아키텍처
```

트레이는 WinForms 라서 리눅스에서 빌드되지 않습니다. `build.sh` 는 CLI 만 만듭니다.
리눅스에서 굳이 컴파일만 확인하려면 `-p:EnableWindowsTargeting=true` 가 필요합니다.

```bash
dotnet build src/VMan.Tray/VMan.Tray.csproj -c Release -r win-x64 \
    --self-contained true -p:EnableWindowsTargeting=true
```

자체 포함 단일 파일로 게시하므로 파일 하나만 배포하면 됩니다. 대신 크기가 큽니다
(CLI 약 35MB, 트레이 약 65MB).

### 프로젝트별 TFM

| 프로젝트 | TFM | 이유 |
|---|---|---|
| `VMan.Core` | `net8.0` | 리눅스에서도 그대로 쓴다. 레지스트리는 `Microsoft.Win32.Registry` 패키지로 |
| `VMan.Cli` | `net8.0` | 양쪽에서 게시 |
| `VMan.Tray` | `net8.0-windows` | WinForms |

`RuntimeIdentifier` / `SelfContained` / `PublishSingleFile` 은 csproj 에 넣지 않고
빌드 스크립트가 넘깁니다. csproj 에 `win-x64` 를 박아두면 리눅스 게시가 막힙니다.

## 셸 확장 (윈도우 11 상단 컨텍스트 메뉴)

`vman menu install` 이 등록하는 고전 메뉴는 윈도우 11 에서 「추가 옵션 표시」 안쪽에
들어갑니다. 상단에 바로 올리려면 MSIX 로 패키징한 `IExplorerCommand` COM 핸들러가
필요합니다.

```
src/VMan.ShellExt/VManShellExt.cpp   IExplorerCommand 구현 (C++, WRL)
packaging/AppxManifest.template.xml  스파스 패키지 매니페스트
packaging/build-msix.ps1             컴파일 → 패키지 → 서명 → 설치
```

```powershell
.\packaging\build-msix.ps1            # 빌드 + 서명
.\packaging\build-msix.ps1 -Install   # + 사이드로드
.\packaging\build-msix.ps1 -Uninstall
```

**필요한 것**

빌드와 서명까지는 **관리자 권한 없이** 됩니다. 자체 서명 인증서가 `CurrentUser\My` 에
만들어지기 때문입니다. 관리자 권한은 (1) 빌드 도구 설치와 (2) 사이드로드할 때
인증서를 시스템 신뢰 저장소에 넣을 때만 필요합니다.

1. Build Tools + C++ 워크로드
   ```powershell
   winget install Microsoft.VisualStudio.2022.BuildTools -e --override `
     "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
   ```
2. 자체 서명 인증서를 `LocalMachine\TrustedPeople` 에 등록 (스크립트가 명령을 알려줍니다)
3. 개발자 모드

### 왜 C++ 인가

이 DLL 은 탐색기가 띄우는 COM 대리자에 로드됩니다. CLR 을 끌어들이면 안 되므로
런타임 의존성이 없어야 합니다. C# NativeAOT 도 가능하지만 어차피 MSVC 링커가
필요해서 준비물이 같습니다.

### 왜 스파스 패키지인가

실제 파일은 패키지에 넣지 않고 `%LOCALAPPDATA%\vman\bin` 을 그대로 가리킵니다.
`vman-tray.exe` 가 66MB 라 복사하면 설치본이 두 벌이 되고 vman 을 다시 빌드할 때마다
패키지도 다시 만들어야 합니다.

외부 경로는 **매니페스트에 적지 않습니다.** `Properties` 의
`uap10:AllowExternalContent` 로 "패키지 밖 파일을 쓰겠다"고 선언만 하고, 실제 경로는
설치할 때 `Add-AppxPackage -ExternalLocation` 으로 넘깁니다.

### 매니페스트 네임스페이스 (여기서 제일 많이 막힙니다)

SDK 스키마(`Windows Kits\10\Include\<버전>\winrt\*.xsd`)를 직접 읽어야 알 수 있는
것들입니다. 셋 다 `makeappx` 가 스키마 검증에서 거부합니다.

| 요소 | 올바른 네임스페이스 | 왜 |
|---|---|---|
| `Extension` / `FileExplorerContextMenus` | **desktop4** | `DesktopManifestSchema_v4.xsd` 에만 정의됨 |
| `ItemType` / `Verb` | **desktop5** | desktop4 의 `Type` 은 `*` 나 `.확장자`만 받습니다(`ST_FileTypeOrStar`). `Directory` 와 `Directory\Background` 를 받는 것은 desktop5 의 `ST_FileTypeOrStarWithDirectory` 뿐이고, v4 의 `FileExplorerContextMenus` 가 `desktop5:ItemType` 을 자식으로 허용합니다 |
| 외부 위치 | 없음 | `windows.externalLocation` 이라는 카테고리는 스키마에 존재하지 않습니다 |

두 스키마 모두 `elementFormDefault="qualified"` 이므로 `Verb` 에도 접두어가 붙습니다.
`desktop10:ItemType` 을 쓰면 `Drive` 와 `DesktopBackground` 까지 걸 수 있습니다.

### 주의

- 소스에 한글 문자열이 있으므로 `cl` 에 **`/utf-8` 이 필요합니다.** 없으면 MSVC 가
  BOM 없는 소스를 시스템 코드페이지(949)로 읽어 `C2001`(상수에 줄 바꿈 문자)로 멈춥니다.
- 매니페스트의 `Publisher` 는 서명 인증서의 `Subject` 와 **글자 하나까지** 같아야 합니다.
- `com:Class Id` 와 `desktop5:Verb Clsid` 는 C++ 의 `__declspec(uuid(...))` 와 같아야 합니다.
- 하위 메뉴를 가지려면 루트가 `GetFlags` 에서 `ECF_HASSUBCOMMANDS` 를 돌려줘야 합니다.
- 폴더 안 빈 공간 우클릭은 `IShellItemArray` 가 비어 있습니다. `IObjectWithSite` 로 받은
  site 에서 `SID_SFolderView` → `IFolderView::GetFolder` 로 현재 폴더를 알아냅니다.

## 격리해서 시험하기

`VMAN_ROOT` 로 루트를 바꾸면 실제 설치본을 건드리지 않고 시험할 수 있습니다.
`node` 는 부속 환경변수가 없어서 환경 쓰기가 전혀 없습니다.

```powershell
$env:VMAN_ROOT = "$env:TEMP\vmantest"

# node.exe 존재 여부만 검사하므로 더미 파일로 충분합니다
$d = "$env:VMAN_ROOT\versions\node\20.15.0"
New-Item -ItemType Directory -Path $d -Force | Out-Null
Set-Content "$d\node.exe" -Value dummy

.\dist\vman.exe list node
.\dist\vman.exe use node 20.15.0
cmd /c "dir `"$env:VMAN_ROOT\current`""   # <JUNCTION> 으로 보이면 정상
```

정리할 때는 `Remove-Item -Recurse` 대신 `cmd /c rmdir /s /q` 를 쓰세요.
PowerShell 이 정션을 따라가 원본을 지우는 사고를 피할 수 있습니다.

리눅스에서는 `HOME` 까지 같이 바꾸면 rc 파일도 진짜와 격리됩니다.

```bash
export VMAN_ROOT=/tmp/vmantest
export HOME=/tmp/vmanhome && mkdir -p $HOME

d=$VMAN_ROOT/versions/node/20.15.0/bin
mkdir -p $d && printf '#!/bin/sh\necho v20.15.0\n' > $d/node && chmod +x $d/node

./dist/vman setup            # $HOME 아래 .profile 에만 쓴다
./dist/vman use node 20.15.0
ls -la $VMAN_ROOT/current    # 심볼릭 링크로 보이면 정상
./dist/vman doctor
```

정리는 `rm -rf $VMAN_ROOT` 로 충분합니다. `current/` 아래는 심볼릭 링크라
`rm -rf` 가 링크만 지우고 대상을 따라가지 않습니다.

## 테마 미리보기 하네스

메뉴 디자인은 눈으로 보지 않으면 못 고칩니다. 트레이 앱에 캡처 모드가 있습니다.

```powershell
.\dist\vman-tray.exe --preview C:\temp\shots
```

`C:\temp\shots` 에 다음이 생깁니다.

| 파일 | 내용 |
|---|---|
| `{테마}-{밝기}-root.png` | 루트 메뉴 |
| `{테마}-{밝기}-sub.png` | 3단계까지 펼친 모습 |
| `{테마}-corner.png` | **실제 트레이 자리(오른쪽 아래)에서 4단계까지** 펼친 화면 전체 |
| `{테마}-{밝기}-metrics.txt` | 메뉴/항목 실제 폭·높이 수치 |

`-corner.png` 가 특히 중요합니다. 화면 가장자리에서만 나타나는 서브메뉴 방향 뒤집힘과
겹침 문제를 여기서 재현할 수 있습니다.

`metrics.txt` 는 레이아웃이 의도대로 잡혔는지 숫자로 확인할 때 씁니다. 예를 들어
`menu.Width` 가 항목 `W` 보다 작으면 텍스트가 잘린다는 뜻입니다.

```
theme=apple MinWidth=208 ArrowSpace=20 RowPaddingX=13
menu.Width=208 ClientWidth=208 Padding={Left=0,Top=5,Right=0,Bottom=5}
  'Python' W=208 H=26 pref=208x26 ... sec='3.11.9' drop=True check=False
```

미리보기 모드는 트레이 아이콘을 등록하지 않습니다 (`Visible = false`).

## 테마 추가하기

[`Theme.cs`](../src/VMan.Tray/Theming/Theme.cs) 에 `Theme` 인스턴스를 하나 더 만들고
`Theme.All` 에 넣으면 메뉴에 자동으로 나타납니다. 색은 밝게/어둡게 두 벌
(`Palette`)이 필요합니다.

치수 항목의 의미:

| 필드 | 뜻 |
|---|---|
| `RowHeight` | 행 높이. 데스크톱 메뉴는 26~42 정도가 자연스럽습니다 |
| `RowPaddingX` | 좌우 안쪽 여백 |
| `ArrowSpace` | 서브메뉴 화살표 예약폭. **텍스트 배치와 같은 값을 씁니다** |
| `MinWidth` | 메뉴 최소 폭 |
| `CompactMinWidth` | 버전 목록처럼 내용이 짧은 깊은 메뉴의 최소 폭 |
| `HighlightInset` / `HighlightRadius` | 선택 표시의 여백과 곡률 |
| `UseDwmCorners` | `true` 면 DWM 이 모서리를 자름(8px 고정, 그림자 깔끔). 큰 반지름은 `false` |

## 새 도구 추가하기

1. [`Layout.cs`](../src/VMan.Core/Layout.cs) 의 `ToolDef` 에 항목 추가
   - `WindowsPathSubDirs` / `UnixPathSubDirs` — `current/{id}` 기준으로 PATH에 넣을 상대 경로
   - `HomeEnvVar` — `JAVA_HOME` 같은 부속 변수 (없으면 `null`)
   - `WindowsProbe` / `UnixProbe` — 유효한 설치본인지 검사할 실행 파일 (`/` 로 적으면 됩니다)
   - `CommandNames` — `doctor` 가 PATH 에서 찾아볼 명령 이름

   OS별로 나눠 받는 이유는 배포본 레이아웃이 실제로 다르기 때문입니다.
   윈도우 Python 은 루트에 `python.exe`, 리눅스는 `bin/python3` 만 있습니다.
2. [`Downloader.cs`](../src/VMan.Core/Downloader.cs) 에 `InstallXxxAsync` 추가
   (`NodeOs` / `AdoptiumOs` / `PythonKeySuffix` 처럼 OS 조각을 분리해 두면 깔끔합니다)
3. [`VersionCatalog.cs`](../src/VMan.Core/VersionCatalog.cs) 의 `FetchAsync` 에 분기 추가
4. [`Program.cs`](../src/VMan.Cli/Program.cs) 의 `install` / `available` 에 분기 추가

목록/전환/트레이 메뉴는 `ToolDef.All` 을 돌기 때문에 자동으로 따라옵니다.

## 주의할 점

- **OS 분기는 반드시 `Platform` 을 거치세요.** `RuntimeInformation.IsOSPlatform` 을
  여기저기 흩뿌리면 분석기가 윈도우 전용 API 호출을 잡아내지 못합니다.
  `Platform.IsWindows` 에는 `[SupportedOSPlatformGuard("windows")]` 가 붙어 있습니다.
- **링크와 환경 조작은 `Links` / `EnvStore` 만 부르세요.** `Junction` · `EnvManager` ·
  `ShellEnv` 를 직접 부르면 한쪽 OS에서만 도는 코드가 됩니다.
- `Environment.SetEnvironmentVariable(..., User)` 를 쓰지 마세요. 이유는
  [ARCHITECTURE.md](ARCHITECTURE.md#3-path-를-다루는-규칙) 참고.
- `Links.Remove` 는 대상이 링크인지 반드시 검사합니다. 이 가드를 빼면 버그 하나가
  실제 JDK 설치본을 재귀 삭제할 수 있습니다.
- `.tar.gz` 는 `TarFile.ExtractToDirectory` 로 풀지 마세요. 이름 잘림뿐 아니라
  리눅스 배포본의 심볼릭 링크와 실행 권한도 잃습니다. 이유는
  [ARCHITECTURE.md](ARCHITECTURE.md#tar-파싱에-관한-주의) 참고.
- 아카이브 형식을 URL 확장자로 판단하지 마세요. Adoptium URL 에는 확장자가 없습니다.
  `Downloader.IsGzip` 이 앞 2바이트를 봅니다.
- `env.sh` 는 POSIX sh 문법만 씁니다. bash 전용 문법(`${var//x/y}`, 배열)을 넣으면
  `~/.profile` 을 읽는 dash 에서 깨집니다.
- **PATH 계산을 셸 스크립트로 옮기지 마세요.** `ShellCode` 가 C#에서 계산하고 셸에는
  대입문 한 줄만 내보내기 때문에 네 가지 셸을 같은 코드로 지원할 수 있습니다.
- `vman env` 는 표준출력에 **코드만** 내보내야 합니다. 안내 문구를 섞으면 그대로
  eval 되어 버립니다. 메시지는 반드시 표준오류로.
- 셸 함수 동작을 시험할 때 `vman ... | tail` 처럼 **파이프 왼쪽에 두지 마세요.**
  파이프라인 왼쪽은 서브셸이라 환경 변경이 사라져서, 되는 것도 안 되는 것처럼 보입니다.
  출력을 줄이고 싶으면 파일로 뺀 뒤(`vman doctor > out 2>&1`) 그 파일을 보세요.
- 탐색기 메뉴에서 **콘솔 앱을 물리지 마세요.** 검은 창이 번쩍이고 결과도 못 보여줍니다.
  `vman-tray.exe --venv` 처럼 WinExe 쪽에 맡기고, 그 모드에서는 트레이 아이콘을
  만들지 않아야 합니다(아이콘이 두 개 됩니다).
- 하위 메뉴를 만들려면 `subcommands` 를 **빈 문자열**로 둬야 합니다. 값이 없으면
  탐색기가 `shell\` 하위키를 펼치지 않습니다.
- 셸 래퍼는 **종료 코드로 거르지 않습니다.** `doctor` 는 문제를 찾으면 1을 돌려주는데
  그 문제가 바로 "이 창이 낡았다" 인 경우가 있어서, 거르면 고치려던 것을 못 고칩니다.
