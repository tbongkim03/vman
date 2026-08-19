#!/usr/bin/env bash
#
# vman 빌드 & 설치 스크립트 (리눅스 / WSL2)
#   ./build.sh              빌드만 → dist/vman
#   ./build.sh --install    빌드 후 ~/.local/share/vman/bin 에 배치하고 setup 실행
#   ./build.sh --rid linux-arm64
#
# 트레이 앱은 WinForms 라서 윈도우 전용이다. 여기서는 CLI 만 만든다.
# 윈도우용 빌드는 build.ps1 을 쓴다.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$root/dist"
install=0
rid=""

while [ $# -gt 0 ]; do
    case "$1" in
        --install|-i) install=1 ;;
        --rid) rid="$2"; shift ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "알 수 없는 옵션: $1" >&2; exit 1 ;;
    esac
    shift
done

# RID 를 안 주면 이 머신의 아키텍처로 정한다.
if [ -z "$rid" ]; then
    case "$(uname -m)" in
        x86_64)         rid="linux-x64" ;;
        aarch64|arm64)  rid="linux-arm64" ;;
        *) echo "지원하지 않는 아키텍처: $(uname -m)" >&2; exit 1 ;;
    esac
fi

if ! command -v dotnet >/dev/null 2>&1; then
    cat >&2 <<'MSG'
dotnet 을 찾을 수 없습니다. .NET 8 SDK 를 설치하세요.

  # 우분투/데비안 (sudo 필요)
  sudo apt-get install -y dotnet-sdk-8.0

  # sudo 없이 홈 디렉터리에 설치
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
  export PATH="$HOME/.dotnet:$PATH"
  export DOTNET_ROOT="$HOME/.dotnet"
MSG
    exit 1
fi

echo "== 빌드 시작 ($rid) =="

rm -rf "$out"
mkdir -p "$out"

dotnet publish "$root/src/VMan.Cli/VMan.Cli.csproj" \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$out"

# 단일 파일 게시 후 남는 부산물 정리
find "$out" -name '*.pdb' -delete 2>/dev/null || true

chmod +x "$out/vman"
echo
echo "빌드 완료: $out/vman  ($(du -h "$out/vman" | cut -f1))"

if [ "$install" -eq 1 ]; then
    # vman 자신이 쓰는 것과 같은 규칙으로 루트를 정한다.
    vman_root="${VMAN_ROOT:-${XDG_DATA_HOME:-$HOME/.local/share}/vman}"
    bin_dir="$vman_root/bin"

    echo
    echo "== 설치 중: $bin_dir =="
    mkdir -p "$bin_dir"
    install -m 755 "$out/vman" "$bin_dir/vman"

    "$bin_dir/vman" setup
fi
