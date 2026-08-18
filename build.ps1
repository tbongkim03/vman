<#
  vman 빌드 & 설치 스크립트
    .\build.ps1            빌드만
    .\build.ps1 -Install   빌드 후 %LOCALAPPDATA%\vman\bin 에 배치하고 setup 실행
#>
param(
    [switch]$Install,
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out  = Join-Path $root "dist"

Write-Host "== 빌드 시작 ($Rid) ==" -ForegroundColor Cyan

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

foreach ($proj in @("src\VMan.Cli\VMan.Cli.csproj", "src\VMan.Tray\VMan.Tray.csproj")) {
    Write-Host "  -> $proj"
    dotnet publish (Join-Path $root $proj) `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패: $proj" }
}

# 단일 파일 게시 후 남는 부산물 정리
Get-ChildItem $out -Include *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "`n빌드 완료: $out" -ForegroundColor Green
Get-ChildItem $out -Filter *.exe | ForEach-Object {
    "{0,-18} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)
}

if ($Install) {
    $binDir = Join-Path $env:LOCALAPPDATA "vman\bin"
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null

    Write-Host "`n== 설치 중: $binDir ==" -ForegroundColor Cyan
    Get-ChildItem $out -Filter *.exe | Copy-Item -Destination $binDir -Force

    & (Join-Path $binDir "vman.exe") setup

    Write-Host "`n트레이 앱을 실행하려면:" -ForegroundColor Yellow
    Write-Host "  $binDir\vman-tray.exe"
}
