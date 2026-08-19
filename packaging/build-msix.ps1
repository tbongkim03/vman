<#
  vman 셸 확장(윈도우 11 상단 컨텍스트 메뉴) 빌드 · 서명 · 설치 스크립트.

    .\build-msix.ps1              빌드 + 패키지 + 서명
    .\build-msix.ps1 -Install     위 + 사이드로드 설치
    .\build-msix.ps1 -Uninstall   제거

  필요한 것 (없으면 안내하고 멈춘다)
    - Visual Studio Build Tools 2022 + "Desktop development with C++" 워크로드
        winget install Microsoft.VisualStudio.2022.BuildTools --override `
          "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
    - 자체 서명 인증서를 LocalMachine\TrustedPeople 에 등록 (관리자 권한)
    - 개발자 모드 또는 사이드로드 허용

  왜 스파스 패키지인가
    실제 파일은 %LOCALAPPDATA%\vman\bin 을 그대로 가리킨다. vman-tray.exe 가 68MB 라
    패키지에 복사하면 설치본이 두 벌이 되고, vman 을 다시 빌드할 때마다 패키지도
    다시 만들어야 한다.
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Uninstall,
    [string]$Configuration = 'Release',
    [string]$CertSubject = 'CN=vman-dev',
    [string]$Version = '1.0.0.0'
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $PSScriptRoot
$srcDir    = Join-Path $root 'src\VMan.ShellExt'
$outDir    = Join-Path $root 'dist\msix'
$vmanBin   = if ($env:VMAN_ROOT) { Join-Path $env:VMAN_ROOT 'bin' }
             else { Join-Path $env:LOCALAPPDATA 'vman\bin' }
$packageId = 'VMan.ShellExt'

# ---------- 제거 ----------

if ($Uninstall) {
    $pkg = Get-AppxPackage -Name $packageId -ErrorAction SilentlyContinue
    if ($pkg) {
        Remove-AppxPackage -Package $pkg.PackageFullName
        Write-Host "제거했습니다: $($pkg.PackageFullName)"
    } else {
        Write-Host '설치되어 있지 않습니다.'
    }
    Remove-Item (Join-Path $vmanBin 'VMan.ShellExt.dll') -Force -ErrorAction SilentlyContinue
    return
}

# ---------- 도구 찾기 ----------

function Find-One([string]$label, [string[]]$patterns, [string]$hint) {
    foreach ($p in $patterns) {
        $f = Get-ChildItem -Path $p -Recurse -ErrorAction SilentlyContinue |
             Sort-Object FullName -Descending | Select-Object -First 1
        if ($f) { return $f.FullName }
    }
    throw "$label 를 찾지 못했습니다.`n  $hint"
}

$vcHint = @'
Visual Studio Build Tools 2022 + "Desktop development with C++" 가 필요합니다.
관리자 PowerShell 에서:
  winget install Microsoft.VisualStudio.2022.BuildTools -e --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
'@

$vcvars = Find-One 'vcvars64.bat' @(
    'C:\Program Files\Microsoft Visual Studio\2022\*\VC\Auxiliary\Build\vcvars64.bat',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\*\VC\Auxiliary\Build\vcvars64.bat') $vcHint

$makeappx = Find-One 'makeappx.exe' @('C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe') $vcHint
$signtool = Find-One 'signtool.exe' @('C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe') $vcHint

Write-Host "vcvars64 : $vcvars"
Write-Host "makeappx : $makeappx"
Write-Host "signtool : $signtool"

if (-not (Test-Path (Join-Path $vmanBin 'vman-tray.exe'))) {
    throw "$vmanBin 에 vman-tray.exe 가 없습니다. 먼저 build.ps1 -Install 로 vman 을 설치하세요."
}

# ---------- 1. DLL 컴파일 ----------

Write-Host "`n== DLL 컴파일 ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dll = Join-Path $outDir 'VMan.ShellExt.dll'

# vcvars 를 먹인 cmd 안에서 cl 을 부른다. MSBuild 프로젝트를 두지 않는 이유는
# 소스가 한 장뿐이라 vcxproj 가 순수한 부담이기 때문이다.
$clArgs = @(
    '/nologo', '/W4', '/WX-', '/EHsc', '/O2', '/MT', '/std:c++17',
    '/D_UNICODE', '/DUNICODE', '/DNDEBUG',
    "`"$srcDir\VManShellExt.cpp`"",
    '/link', '/DLL', '/NOLOGO',
    "/DEF:`"$srcDir\VMan.ShellExt.def`"",
    "/OUT:`"$dll`"",
    'ole32.lib', 'shlwapi.lib', 'shell32.lib', 'user32.lib', 'runtimeobject.lib'
) -join ' '

$build = "call `"$vcvars`" >nul && cl $clArgs"
& cmd.exe /c $build
if ($LASTEXITCODE -ne 0) { throw "컴파일 실패 (종료 코드 $LASTEXITCODE)" }
Write-Host "만들어짐: $dll"

# 셸 확장 DLL 은 매니페스트가 가리키는 외부 위치(= vman bin)에 있어야 한다.
Copy-Item $dll (Join-Path $vmanBin 'VMan.ShellExt.dll') -Force

# ---------- 2. 인증서 ----------

Write-Host "`n== 서명 인증서 ==" -ForegroundColor Cyan
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "만드는 중: $CertSubject"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $CertSubject `
        -KeyUsage DigitalSignature -FriendlyName 'vman shell extension (dev)' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -NotAfter (Get-Date).AddYears(3)
}
Write-Host "지문: $($cert.Thumbprint)"

# 패키지의 Publisher 는 인증서 Subject 와 글자 하나까지 같아야 한다.
$publisher = $cert.Subject

# ---------- 3. 패키지 레이아웃 ----------

Write-Host "`n== 패키지 구성 ==" -ForegroundColor Cyan
$stage = Join-Path $outDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Assets') | Out-Null

$manifest = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.template.xml') -Raw).
    Replace('{{PUBLISHER}}', $publisher).
    Replace('{{VERSION}}', $Version).
    Replace('{{EXTERNAL_LOCATION}}', $vmanBin)
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# 로고는 있기만 하면 된다(앱 목록에 안 나오는 껍데기라). 단색 PNG 를 만든다.
Add-Type -AssemblyName System.Drawing
foreach ($logo in @(@{n='StoreLogo.png';s=50}, @{n='Square150x150Logo.png';s=150}, @{n='Square44x44Logo.png';s=44})) {
    $bmp = New-Object System.Drawing.Bitmap($logo.s, $logo.s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 32, 96, 176))
    $g.Dispose()
    $bmp.Save((Join-Path $stage "Assets\$($logo.n)"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ---------- 4. 패키지 + 서명 ----------

Write-Host "`n== makeappx / signtool ==" -ForegroundColor Cyan
$msix = Join-Path $outDir 'VMan.ShellExt.msix'
Remove-Item $msix -Force -ErrorAction SilentlyContinue

& $makeappx pack /d $stage /p $msix /nv
if ($LASTEXITCODE -ne 0) { throw "makeappx 실패 (종료 코드 $LASTEXITCODE)" }

& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE -ne 0) { throw "서명 실패 (종료 코드 $LASTEXITCODE)" }

Write-Host "`n패키지: $msix" -ForegroundColor Green

# ---------- 5. 설치 ----------

if (-not $Install) {
    Write-Host @"

설치하려면 -Install 을 주세요. 그 전에 아래 두 가지가 필요합니다.

  1) 인증서를 신뢰 저장소에 등록  (관리자 PowerShell)
     Export-Certificate -Cert Cert:\CurrentUser\My\$($cert.Thumbprint) -FilePath vman-dev.cer
     Import-Certificate -FilePath vman-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

  2) 개발자 모드 켜기
     설정 → 시스템 → 개발자용 → 개발자 모드
"@
    return
}

Write-Host "`n== 설치 ==" -ForegroundColor Cyan
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
           Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $trusted) {
    throw @"
인증서가 LocalMachine\TrustedPeople 에 없어서 설치할 수 없습니다.
관리자 PowerShell 에서:
  Export-Certificate -Cert Cert:\CurrentUser\My\$($cert.Thumbprint) -FilePath `$env:TEMP\vman-dev.cer
  Import-Certificate -FilePath `$env:TEMP\vman-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
"@
}

Add-AppxPackage -Path $msix -ExternalLocation $vmanBin
Write-Host "설치했습니다. 폴더를 우클릭하면 상단 메뉴에 바로 보입니다." -ForegroundColor Green
Write-Host "안 보이면 탐색기를 다시 시작하세요: Stop-Process -Name explorer -Force"
