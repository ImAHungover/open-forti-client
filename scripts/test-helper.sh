#!/bin/sh
# Builds and runs the tests of the privileged helper's session logic, as the normal user, against a
# fake openfortivpn. No root, no launchd, no XPC. See docs/development.md, "Tests".
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

xcrun swiftc -target arm64-apple-macos14.0 -o "$OUT/helper-tests" \
    "$ROOT/src/OpenFortiClient.Helper/OpenFortiVpnSession.swift" \
    "$ROOT/tests/OpenFortiClient.Helper.Tests/main.swift"
"$OUT/helper-tests"
