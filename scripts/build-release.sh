#!/bin/sh
# Builds the release app (Apple Silicon), signed with the development certificate from
# src/OpenFortiClient.App/Signing.local.props, and packs it into artifacts/release/OpenFortiClient-<version>.dmg.
# Not notarised: that needs a paid Apple Developer account (phase 4b). See docs/development.md, "Release build".
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/OpenFortiClient.App"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$PROJECT/OpenFortiClient.App.csproj")"

if [ ! -f "$PROJECT/Signing.local.props" ]; then
    echo "No signing set up: run ./scripts/provision-signing.sh <TEAM_ID> first (the helper needs a signed app)." >&2
    exit 1
fi

rm -rf "$PROJECT/bin/Release"
dotnet publish "$PROJECT" -f net10.0-maccatalyst -c Release -r maccatalyst-arm64 -p:CreatePackage=false -v quiet -nologo
APP="$PROJECT/bin/Release/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient.app"
codesign --verify --deep --strict "$APP"

# A free Personal Team profile lasts 7 days; after that the installed app no longer launches.
PROFILE="$APP/Contents/embedded.provisionprofile"
if [ -f "$PROFILE" ]; then
    EXPIRES="$(security cms -D -i "$PROFILE" | plutil -extract ExpirationDate raw -)"
    echo "Signing profile expires $EXPIRES. Rebuild after ./scripts/provision-signing.sh before then."
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
ditto "$APP" "$STAGE/OpenFortiClient.app"
ln -s /Applications "$STAGE/Applications"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$ROOT/scripts/install.sh" "$STAGE/"

OUT="$ROOT/artifacts/release"
mkdir -p "$OUT"
DMG="$OUT/OpenFortiClient-$VERSION.dmg"
hdiutil create -volname "OpenFortiClient $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
echo "Built $DMG"
shasum -a 256 "$DMG"
