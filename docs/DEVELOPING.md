# 개발

## 빌드

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 가 필요합니다.

```powershell
dotnet build VMan.sln -c Release          # 컴파일만 (빠름)
powershell -ExecutionPolicy Bypass -File .\build.ps1           # 단일 exe 게시 → dist\
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install  # 게시 + 설치 + setup
```

자체 포함 단일 파일로 게시하므로 exe 하나만 배포하면 됩니다. 대신 크기가 큽니다
(CLI 약 34MB, 트레이 약 65MB).

## 격리해서 시험하기

`VMAN_ROOT` 환경변수로 루트를 바꾸면 실제 PATH와 레지스트리를 건드리지 않고 시험할 수
있습니다. `node` 는 부속 환경변수가 없어서 레지스트리 쓰기가 전혀 없습니다.

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
   - `PathSubDirs` — `current\{id}` 기준으로 PATH에 넣을 상대 경로
   - `HomeEnvVar` — `JAVA_HOME` 같은 부속 변수 (없으면 `null`)
   - `ProbeExe` — 유효한 설치본인지 검사할 실행 파일
2. [`Downloader.cs`](../src/VMan.Core/Downloader.cs) 에 `InstallXxxAsync` 추가
3. [`VersionCatalog.cs`](../src/VMan.Core/VersionCatalog.cs) 의 `FetchAsync` 에 분기 추가
4. [`Program.cs`](../src/VMan.Cli/Program.cs) 의 `install` / `available` 에 분기 추가

목록/전환/트레이 메뉴는 `ToolDef.All` 을 돌기 때문에 자동으로 따라옵니다.

## 주의할 점

- `Environment.SetEnvironmentVariable(..., User)` 를 쓰지 마세요. 이유는
  [ARCHITECTURE.md](ARCHITECTURE.md#3-path-를-다루는-규칙) 참고.
- `Junction.Remove` 는 대상이 정션인지 반드시 검사합니다. 이 가드를 빼면 버그 하나가
  실제 JDK 설치본을 재귀 삭제할 수 있습니다.
- `.tar.gz` 는 `TarFile.ExtractToDirectory` 로 풀지 마세요. 이유는
  [ARCHITECTURE.md](ARCHITECTURE.md#tar-파싱에-관한-주의) 참고.
