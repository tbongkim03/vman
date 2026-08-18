# 문제 해결

## 버전을 바꿨는데 터미널에 반영되지 않습니다

**새 터미널을 여세요.** 이미 열려 있는 창은 시작 시점의 환경 블록 사본을 계속 씁니다.
이건 vman 의 버그가 아니라 윈도우 프로세스 모델입니다.

지금 창에서만 급하게 확인하려면:

```powershell
$env:PATH = "$env:LOCALAPPDATA\vman\current\python;$env:LOCALAPPDATA\vman\current\python\Scripts;$env:PATH"
```

## `이 시스템에서 스크립트를 실행할 수 없으므로...`

PowerShell 실행 정책 때문입니다. 시스템 설정을 바꾸지 않고 이번 실행만 허용합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

## `dotnet` 명령을 찾을 수 없습니다

.NET SDK 설치 직후 열려 있던 터미널이라 PATH가 낡은 것입니다. 새 터미널을 여세요.
확인은 이렇게 합니다.

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" --list-sdks
```

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

## `정션이 아닌 실제 폴더입니다. 안전을 위해 삭제하지 않습니다`

`current\<도구>` 자리에 정션이 아닌 진짜 폴더가 있다는 뜻입니다. 의도적으로 만든 게
아니라면 내용을 확인하고 직접 지운 뒤 다시 `vman use` 를 실행하세요.
이 검사는 실수로 런타임 설치본이 지워지는 것을 막기 위한 것입니다.

## SHA256 이 일치하지 않습니다

다운로드가 중간에 끊겼거나 프록시가 내용을 바꾼 경우입니다. 다시 시도하세요.
계속 실패하면 회사 네트워크의 TLS 검사 프록시를 의심해볼 만합니다.

## PATH 를 되돌리고 싶습니다

```powershell
vman unsetup
```

PATH 와 `JAVA_HOME` 에서 vman 항목을 제거합니다. 설치본은 `versions\` 에 남습니다.
수정 전 원본은 항상 백업되어 있습니다.

```powershell
Get-ChildItem "$env:LOCALAPPDATA\vman\backup"
```

## SmartScreen 경고가 뜹니다

코드 서명 인증서 없이 빌드한 exe 라서 그렇습니다. 직접 빌드한 바이너리라면
"추가 정보 → 실행"으로 넘어가면 됩니다.

## 버전 목록이 비어 있거나 오래됐습니다

목록은 12시간 캐시됩니다. 강제로 갱신하려면 캐시 파일을 지우세요.

```powershell
Remove-Item "$env:LOCALAPPDATA\vman\downloads\catalog-*.json"
Remove-Item "$env:LOCALAPPDATA\vman\downloads\python-index.json"
```
