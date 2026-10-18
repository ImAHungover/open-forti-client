#!/bin/sh
# Builds the end-to-end copy of the app (bundle io.github.openforticlient.e2e) and runs the UI tests
# against it through Appium. Your normal app and its data are not touched, and nothing runs as root.
# See docs/development.md, "End-to-end UI tests".
#   ./scripts/run-ui-tests.sh                      all UI tests, Debug build of the test copy (about 9 minutes)
#   ./scripts/run-ui-tests.sh --quick              one test per main flow (about 2 minutes), for everyday use
#   ./scripts/run-ui-tests.sh --release            Release (trimmed) build of the test copy
#   ./scripts/run-ui-tests.sh --filter Fails_over  extra arguments go to dotnet test
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION=Debug
QUICK=""
while [ $# -gt 0 ]; do
    case "$1" in
        --release) CONFIGURATION=Release; shift ;;
        --quick) QUICK="Suite=Quick"; shift ;;
        *) break ;;
    esac
done

if ! command -v appium >/dev/null 2>&1 || ! appium driver list --installed 2>&1 | grep -q mac2; then
    echo "Appium with the mac2 driver is needed: see docs/development.md, \"End-to-end UI tests\"." >&2
    exit 1
fi

# Incremental builds are much faster, but can keep an old Info.plist in the bundle (docs/development.md):
# rebuild from scratch only when Info.plist or the entitlements are newer than the built app.
APP="$ROOT/artifacts/e2e/bin/$CONFIGURATION/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient E2E.app"
FULL=""
for source in "$ROOT"/src/OpenFortiClient.App/Platforms/MacCatalyst/*.plist; do
    if [ ! -f "$APP/Contents/Info.plist" ] || [ "$source" -nt "$APP/Contents/Info.plist" ]; then
        FULL="--no-incremental"
    fi
done
dotnet build "$ROOT/src/OpenFortiClient.App" -f net10.0-maccatalyst -c "$CONFIGURATION" -p:RuntimeIdentifier=maccatalyst-arm64 \
    -p:E2E=true $FULL -v quiet -nologo
if [ -n "$QUICK" ]; then
    set -- --filter "$QUICK" "$@"
fi
OPENFORTICLIENT_E2E_CONFIGURATION="$CONFIGURATION" \
    dotnet test "$ROOT/tests/OpenFortiClient.App.UiTests" --logger "console;verbosity=normal" "$@"
