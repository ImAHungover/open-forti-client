#!/bin/sh
# Builds the release app (Apple Silicon) with the signing from src/OpenFortiClient.App/Signing.local.props
# and packs it into artifacts/release/OpenFortiClient-<version>.dmg. Next to it: the exact sources of the
# bundled openfortivpn and OpenSSL (published with each release, as the GPL requires) and SHA256SUMS.
# With a Developer ID certificate the DMG is signed too and is ready for scripts/notarize.sh.
# See docs/release.md.
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/OpenFortiClient.App"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$PROJECT/OpenFortiClient.App.csproj")"

if [ ! -f "$PROJECT/Signing.local.props" ]; then
    echo "No signing set up: run ./scripts/provision-signing.sh <TEAM_ID> first (the helper needs a signed app)." >&2
    exit 1
fi
CODESIGN_KEY="$(sed -n 's:.*<CodesignKey>\(.*\)</CodesignKey>.*:\1:p' "$PROJECT/Signing.local.props")"
# CodesignKey is the certificate's SHA-1 hash (or, written by hand, its name); the name tells the kind.
IDENTITY="$(security find-identity -v -p codesigning | grep -F "$CODESIGN_KEY" | sed -n 's/.*"\(.*\)".*/\1/p' | head -n 1)"
if [ -z "$IDENTITY" ]; then
    echo "No signing identity \"$CODESIGN_KEY\" in the keychain: run ./scripts/provision-signing.sh <TEAM_ID> again." >&2
    exit 1
fi

"$ROOT/scripts/build-openfortivpn.sh"
rm -rf "$PROJECT/bin/Release"
dotnet publish "$PROJECT" -f net10.0-maccatalyst -c Release -r maccatalyst-arm64 -p:CreatePackage=false -v quiet -nologo
APP="$PROJECT/bin/Release/net10.0-maccatalyst/maccatalyst-arm64/OpenFortiClient.app"
codesign --verify --deep --strict "$APP"

PROFILE="$APP/Contents/embedded.provisionprofile"
if [ -f "$PROFILE" ]; then
    echo "Signing profile expires $(security cms -D -i "$PROFILE" | plutil -extract ExpirationDate raw -)."
fi
case "$IDENTITY" in
    "Developer ID Application:"*) ;;
    *) echo "Signed with \"$IDENTITY\": for this Mac only. Releases need Developer ID (docs/release.md)." ;;
esac

STAGE="$(mktemp -d)/OpenFortiClient"
trap 'rm -rf "$(dirname "$STAGE")"' EXIT
mkdir -p "$STAGE"
ditto "$APP" "$STAGE/OpenFortiClient.app"
ln -s /Applications "$STAGE/Applications"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$ROOT/scripts/install.sh" "$STAGE/"

OUT="$ROOT/artifacts/release"
rm -rf "$OUT"
mkdir -p "$OUT"
DMG="$OUT/OpenFortiClient-$VERSION.dmg"
diskutil image create from --format ULFO --volumeName "OpenFortiClient $VERSION" "$STAGE" "$DMG" >"$STAGE.log" 2>&1 \
    || { cat "$STAGE.log" >&2; exit 1; }
case "$IDENTITY" in
    "Developer ID Application:"*) codesign --sign "$CODESIGN_KEY" --timestamp "$DMG" ;;
esac

OPENFORTIVPN_VERSION="$(sed -n 's/^OPENFORTIVPN_VERSION=//p' "$ROOT/scripts/build-openfortivpn.sh")"
cp "$ROOT/artifacts/openfortivpn/$OPENFORTIVPN_VERSION/sources/"*.tar.gz "$OUT/"
(cd "$OUT" && shasum -a 256 -- *.dmg *.tar.gz > SHA256SUMS)
echo "Built $DMG"
cat "$OUT/SHA256SUMS"
