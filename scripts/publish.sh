#!/usr/bin/env bash
# 发布打包脚本：self-contained 单文件 + tar.gz + SHA256。
#
# 用法（在仓库任意位置）：
#   scripts/publish.sh                      # 默认 win-x64 + linux-x64
#   RIDS="win-x64" scripts/publish.sh       # 只打 Windows
#   VERSION=0.1.1 scripts/publish.sh        # 覆盖版本（默认读 Directory.Build.props）
#
# 产物（artifacts/dist，已被 .gitignore 忽略）：
#   ironst-<版本>-<rid>.tar.gz   内含 ironst(.exe) + ironst-ls(.exe) + LICENSE / README / CHANGELOG
#   SHA256SUMS.txt
#
# 为什么是 tar.gz 而不是 zip：Windows 10+ 自带 tar，Git Bash / Linux / macOS
# 也都有；zip 在 Git Bash 里不一定存在，会让“一条命令打包”失效。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${VERSION:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -1)}"
if [ -z "$VERSION" ]; then
    echo "错误：无法从 Directory.Build.props 读取 <Version>" >&2
    exit 1
fi

RIDS="${RIDS:-win-x64 linux-x64}"
CONFIG="${CONFIG:-Release}"
OUT="$ROOT/artifacts"
DIST="$OUT/dist"

rm -rf "$OUT/publish" "$OUT/stage" "$DIST"
mkdir -p "$DIST"

for rid in $RIDS; do
    echo "==> 发布 $rid（版本 $VERSION）"
    stage="$OUT/stage/ironst-$VERSION-$rid"
    mkdir -p "$stage"

    # CLI 与语言服务器是面向使用者的两个可执行入口；
    # IDE 是 Windows-only WPF，不进入这组跨平台产物（见 docs/生产发布检查表.md D5）。
    for proj in IronST.CLI/IronST.CLI.csproj IronST.LanguageServer/IronST.LanguageServer.csproj; do
        dotnet publish "$proj" \
            -c "$CONFIG" \
            -r "$rid" \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:EnableCompressionInSingleFile=true \
            -p:DebugType=none \
            -o "$stage" > /dev/null
    done

    # IronST.CLI.csproj 把仓库里的 test.st 当开发样例复制到输出目录；
    # 它不被任何命令引用，不进分发包。
    rm -f "$stage/test.st"

    # 不随包分发 tools/opt：
    #   · IRBuilder.csproj 的 Content 不按 RID 过滤，交叉发布会把 win-x64/opt.exe
    #     原样塞进 linux 包；
    #   · 单文件产物里 Assembly.Location 为空，OptRunner 的两条“程序集同目录”查找
    #     都不会命中。
    # `--bc` 需要的系统 opt 由用户装到 PATH（README「优化级别」已说明）。
    rm -rf "$stage/tools"

    cp LICENSE README.md CHANGELOG.md "$stage"/
    tar -czf "$DIST/ironst-$VERSION-$rid.tar.gz" -C "$OUT/stage" "ironst-$VERSION-$rid"
    echo "    -> $DIST/ironst-$VERSION-$rid.tar.gz"
done

( cd "$DIST" && sha256sum ./*.tar.gz > SHA256SUMS.txt )

echo
echo "完成："
ls -lh "$DIST"
echo
cat "$DIST/SHA256SUMS.txt"
