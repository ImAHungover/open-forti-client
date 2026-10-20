#!/bin/sh
# Notarises and staples the release DMG built by scripts/build-release.sh with a Developer ID, then checks
# that Gatekeeper accepts it. One-time setup: the "openforticlient-notary" keychain profile (docs/release.md),
# or set NOTARY_KEY, NOTARY_KEY_ID and NOTARY_ISSUER for an App Store Connect API key (CI).
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$ROOT/src/OpenFortiClient.App/OpenFortiClient.App.csproj")"
DMG="${1:-$ROOT/artifacts/release/OpenFortiClient-$VERSION.dmg}"

if ! codesign -dvv "$DMG" 2>&1 | grep -q '^Authority=Developer ID Application:'; then
    echo "$DMG is not signed with a Developer ID. Build it with Developer ID signing first (docs/release.md)." >&2
    exit 1
fi

if [ -n "${NOTARY_KEY:-}" ]; then
    set -- --key "$NOTARY_KEY" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER"
else
    set -- --keychain-profile openforticlient-notary
fi

RESULT="$(mktemp)"
trap 'rm -f "$RESULT"' EXIT
echo "Submitting $(basename "$DMG") to Apple's notary service (usually a few minutes)…"
xcrun notarytool submit "$DMG" "$@" --wait --output-format plist > "$RESULT"
STATUS="$(plutil -extract status raw "$RESULT")"
ID="$(plutil -extract id raw "$RESULT")"
if [ "$STATUS" != "Accepted" ]; then
    echo "Notarisation $STATUS. Apple's log:" >&2
    xcrun notarytool log "$ID" "$@" >&2 || true
    exit 1
fi

xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"
spctl --assess --type open --context context:primary-signature -vv "$DMG"

# The DMG changed when the ticket was stapled.
OUT="$(dirname "$DMG")"
if [ -f "$OUT/SHA256SUMS" ]; then
    (cd "$OUT" && shasum -a 256 -- *.dmg *.tar.gz > SHA256SUMS)
fi
echo "Notarised: $DMG"
shasum -a 256 "$DMG"
