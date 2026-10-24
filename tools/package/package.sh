#!/bin/bash
# Release packaging: a self-contained game folder per platform, zipped (docs/DEV_GUIDE.md "Packaging").
#   bash tools/package/package.sh [linux|win|all] [-o OUTDIR]      (default: linux, OUTDIR = <repo>/release)
# Output: OUTDIR/RtsGame-<version>-linux-x64.tar.gz (and -win-x64.zip), each holding one RtsGame/ folder with the
# executable (Rts.Client), data/, release.txt (version label = release-mode marker), rtsgame.png, rtsgame.desktop,
# install-desktop.sh and README.txt. Builds in a temp dir (--artifacts-path), never in the repo's bin/ or obj/,
# so it is safe while another agent builds the same worktree. No trimming, single-file or ReadyToRun: the packaged
# game runs exactly the code `dotnet run` runs.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
here="$root/tools/package"
target=linux
out="$root/release"
while [ $# -gt 0 ]; do
    case "$1" in
        linux|win|all) target=$1 ;;
        -o) out=$2; shift ;;
        -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
        *) echo "usage: $0 [linux|win|all] [-o OUTDIR]" >&2; exit 2 ;;
    esac
    shift
done
mkdir -p "$out"
out=$(cd "$out" && pwd)

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/Rts.Client/Rts.Client.csproj" | head -1)
[ -n "$version" ] || { echo "no <Version> in src/Rts.Client/Rts.Client.csproj" >&2; exit 1; }
rev=$(git -C "$root" describe --always --dirty 2>/dev/null || echo nogit)
label="$version+$rev"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/rtspkg.XXXXXX")
trap 'rm -rf "$tmp"' EXIT
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
echo "Packaging RTS $label ($target) into $out"

# publish RID STAGE: self-contained Release build of the client into STAGE.
publish() {
    local rid=$1 stage=$2; shift 2
    local t0=$SECONDS
    # SourceRevisionId: the SDK appends it to InformationalVersion ("0.7.0+<rev>", AppVersion shows it).
    dotnet publish "$root/src/Rts.Client/Rts.Client.csproj" -c Release -r "$rid" --self-contained true \
        --artifacts-path "$tmp/artifacts-$rid" -o "$stage" -nr:false -p:SourceRevisionId="$rev" \
        -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false "$@" -v:q -nologo || return 1
    echo "  publish $rid: $((SECONDS - t0)) s"
}

# common STAGE: data, marker, desktop files, readme.
common() {
    local stage=$1
    cp -r "$root/data" "$stage/data"
    printf '%s\n' "$label" > "$stage/release.txt"
    cat > "$stage/README.txt" <<EOF
RTS $label - a Supreme Commander-like real-time strategy game.
Run: ./Rts.Client (Linux) or Rts.Client.exe (Windows). Keep data/ and release.txt next to it.
Linux menu entry + desktop shortcut: ./install-desktop.sh (remove: ./install-desktop.sh --uninstall).
Your files: saves, replays, screenshots, crash.log in ~/.local/share/RtsGame (Windows: %APPDATA%\\RtsGame);
settings in ~/.config/RtsGame/settings.json. F12 in game = screenshot (into the folder the game was started from;
the desktop shortcut starts it in ~/.local/share/RtsGame/screenshots).
EOF
}

icon=""
if [ "$target" = linux ] || [ "$target" = all ]; then
    stage="$tmp/linux/RtsGame"
    publish linux-x64 "$stage"
    common "$stage"
    cp "$here/rtsgame.desktop" "$here/install-desktop.sh" "$stage/"
    chmod 755 "$stage/Rts.Client" "$stage/install-desktop.sh"
    chmod 644 "$stage/rtsgame.desktop"
    "$stage/Rts.Client" --export-icon "$stage/rtsgame.png" >/dev/null
    icon="$stage/rtsgame.png"
    archive="$out/RtsGame-$version-linux-x64.tar.gz"
    tar -C "$tmp/linux" --owner=0 --group=0 --numeric-owner -czf "$archive" RtsGame
    echo "  -> $archive ($(du -h "$archive" | cut -f1))"
fi

if [ "$target" = win ] || [ "$target" = all ]; then
    stage="$tmp/win/RtsGame"
    extra=()
    if [ -n "$icon" ]; then
        # Windows exe icon: an .ico whose single entry is the 256x256 PNG (valid since Vista).
        python3 -I - "$icon" "$tmp/rtsgame.ico" <<'PY'
import struct, sys
png = open(sys.argv[1], 'rb').read()
with open(sys.argv[2], 'wb') as f:
    f.write(struct.pack('<HHH', 0, 1, 1) + struct.pack('<BBBBHHII', 0, 0, 0, 0, 1, 32, len(png), 22) + png)
PY
        extra+=("-p:ApplicationIcon=$tmp/rtsgame.ico")
    fi
    if publish win-x64 "$stage" "${extra[@]}"; then
        common "$stage"
        [ -z "$icon" ] || cp "$icon" "$stage/rtsgame.png"
        archive="$out/RtsGame-$version-win-x64.zip"
        rm -f "$archive"
        (cd "$tmp/win" && python3 -I -m zipfile -c "$archive" RtsGame)
        echo "  -> $archive ($(du -h "$archive" | cut -f1))"
    else
        echo "  win-x64 publish failed (runtime packs not restorable?): Windows package skipped" >&2
    fi
fi
