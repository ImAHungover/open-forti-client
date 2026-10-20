#!/bin/sh
# Installs OpenFortiClient into /Applications, or removes it again.
#   ./install.sh [path/to/OpenFortiClient.app]   install (default: the app next to this script, or the release build)
#   ./install.sh --uninstall [--purge]           remove the app and its helper; --purge also removes settings and logs
# See README.md, "Install".
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
TARGET="/Applications/OpenFortiClient.app"
HELPER="io.github.openforticlient.helper"

ask() {
    printf '%s [y/N] ' "$1"
    read -r answer
    [ "$answer" = "y" ] || [ "$answer" = "Y" ]
}

quit_app() {
    osascript -e 'tell application id "io.github.openforticlient" to quit' >/dev/null 2>&1 || true
    sleep 2
}

if [ "${1:-}" = "--uninstall" ]; then
    quit_app
    # The app's Settings → Privileged helper → Remove unregisters it properly; this covers an app that is gone.
    if sudo launchctl print "system/$HELPER" >/dev/null 2>&1; then
        echo "Stopping the privileged helper (needs your password)."
        sudo launchctl bootout "system/$HELPER" || true
    fi
    rm -rf "$TARGET"
    echo "Removed $TARGET."
    if [ "${2:-}" = "--purge" ]; then
        rm -rf "$HOME/Library/Application Support/OpenFortiClient" "$HOME/Library/Logs/OpenFortiClient"
        echo "Removed settings, connections and logs. Saved passwords stay in the Keychain until you delete"
        echo "the connections in the app before uninstalling, or remove them in Keychain Access."
    fi
    exit 0
fi

SOURCE="${1:-}"
if [ -z "$SOURCE" ]; then
    for candidate in "$HERE/OpenFortiClient.app" "$HERE/../src/OpenFortiClient.App/bin/Release/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient.app"; do
        if [ -d "$candidate" ]; then
            SOURCE="$candidate"
            break
        fi
    done
fi

if [ -z "$SOURCE" ] || [ ! -d "$SOURCE" ]; then
    echo "OpenFortiClient.app not found. Pass its path, or build it with ./scripts/build-release.sh." >&2
    exit 1
fi

quit_app
rm -rf "$TARGET"
ditto "$SOURCE" "$TARGET"
echo "Installed $TARGET."

# Release builds use the privileged helper and their own openfortivpn; the development-time sudoers rule
# is no longer needed.
if [ -f /etc/sudoers.d/openfortivpn ] && ask "Remove the old sudoers rule for openfortivpn (needs your password)?"; then
    sudo rm /etc/sudoers.d/openfortivpn
fi

open "$TARGET"
echo "Next: in OpenFortiClient, open Settings → Privileged helper → Install, then allow it in"
echo "System Settings → General → Login Items."
