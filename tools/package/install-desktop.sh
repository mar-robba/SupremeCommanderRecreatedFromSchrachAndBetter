#!/bin/bash
# Adds RTS to the desktop menu and puts a shortcut on the desktop (current user only, no root).
#   ./install-desktop.sh              install (run it from the unpacked RtsGame folder, wherever it lives)
#   ./install-desktop.sh --uninstall  remove the menu entry, icon and desktop shortcut
# Moving the folder later? Run it again. Saves, replays and screenshots live in ~/.local/share/RtsGame (kept on
# uninstall), settings in ~/.config/RtsGame. Made by tools/package/package.sh (docs/DEV_GUIDE.md "Packaging").
set -euo pipefail
dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
data_home=${XDG_DATA_HOME:-}
[ -n "$data_home" ] || data_home="$HOME/.local/share"
apps="$data_home/applications"
icons="$data_home/icons/hicolor/256x256/apps"
desktop_dir=$(xdg-user-dir DESKTOP 2>/dev/null || true)
# xdg-user-dir falls back to $HOME when no desktop folder is configured: use ~/Desktop then.
if [ -z "$desktop_dir" ] || [ "$desktop_dir" = "$HOME" ]; then desktop_dir="$HOME/Desktop"; fi
user_data="$data_home/RtsGame"

if [ "${1:-}" = "--uninstall" ]; then
    rm -f "$apps/rtsgame.desktop" "$icons/rtsgame.png" "$desktop_dir/rtsgame.desktop"
    command -v update-desktop-database >/dev/null && update-desktop-database "$apps" 2>/dev/null || true
    echo "Removed the RTS menu entry, icon and desktop shortcut (your saves in $user_data are kept)."
    exit 0
fi
[ $# -eq 0 ] || { echo "usage: $0 [--uninstall]" >&2; exit 2; }

exe="$dir/Rts.Client"
[ -f "$exe" ] || { echo "Rts.Client not found next to this script ($dir)" >&2; exit 1; }
chmod +x "$exe" 2>/dev/null || true
mkdir -p "$apps" "$icons" "$desktop_dir" "$user_data/screenshots"
cp "$dir/rtsgame.png" "$icons/rtsgame.png"
# Path= is the working directory: raylib's built-in F12 screenshots land in the user screenshots folder.
sed -e "s|@EXEC@|$exe|" -e "s|@PATH@|$user_data/screenshots|" -e "s|@ICON@|$icons/rtsgame.png|" \
    "$dir/rtsgame.desktop" > "$apps/rtsgame.desktop"
chmod +x "$apps/rtsgame.desktop"
cp "$apps/rtsgame.desktop" "$desktop_dir/rtsgame.desktop"
chmod +x "$desktop_dir/rtsgame.desktop"
# GNOME only launches desktop files marked trusted; ignore failures (other desktops, no session bus).
command -v gio >/dev/null && gio set "$desktop_dir/rtsgame.desktop" metadata::trusted true 2>/dev/null || true
command -v update-desktop-database >/dev/null && update-desktop-database "$apps" 2>/dev/null || true
echo "Installed: $apps/rtsgame.desktop"
echo "Desktop shortcut: $desktop_dir/rtsgame.desktop"
echo "Icon: $icons/rtsgame.png"
