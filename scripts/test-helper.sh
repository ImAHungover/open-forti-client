#!/bin/sh
# Builds and runs the tests of the privileged helper's session logic, as the normal user, against a
# fake openfortivpn. No root, no launchd, no XPC. See docs/development.md, "Tests".
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

# -O, as the helper itself is built (OpenFortiClient.App.csproj): an optimiser difference once hid a bug.
xcrun swiftc -target arm64-apple-macos14.0 -O -o "$OUT/helper-tests" \
    "$ROOT/src/OpenFortiClient.Helper/OpenFortiVpnSession.swift" \
    "$ROOT/src/OpenFortiClient.Helper/CodeSignature.swift" \
    "$ROOT/src/OpenFortiClient.Helper/VpnSettingsWriter.swift" \
    "$ROOT/tests/OpenFortiClient.Helper.Tests/main.swift"

# The signature check needs a binary signed like the bundled openfortivpn: with the signing set up for the
# app (Signing.local.props). Without one, those tests are skipped.
PROPS="$ROOT/src/OpenFortiClient.App/Signing.local.props"
if [ -f "$PROPS" ]; then
    IDENTITY="$(sed -n 's:.*<CodesignKey>\(.*\)</CodesignKey>.*:\1:p' "$PROPS")"
    export HELPER_TESTS_TEAM="$(sed -n 's:.*<DevelopmentTeam>\(.*\)</DevelopmentTeam>.*:\1:p' "$PROPS")"
    export HELPER_TESTS_SIGNED="$OUT/signed-openfortivpn"
    printf 'int main(void) { return 0; }\n' | xcrun clang -x c -target arm64-apple-macos14.0 -o "$HELPER_TESTS_SIGNED" -
    codesign --sign "$IDENTITY" --options runtime --timestamp=none --identifier io.github.openforticlient.openfortivpn "$HELPER_TESTS_SIGNED"
fi
"$OUT/helper-tests"
