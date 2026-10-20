#!/bin/sh
# Builds the openfortivpn that ships inside OpenFortiClient.app, from pinned and checksummed sources:
# OpenSSL (static, LTS) and openfortivpn linked against it, for Apple Silicon. Output:
#   artifacts/openfortivpn/<version>/openfortivpn          unsigned; the app build signs it
#   artifacts/openfortivpn/<version>/licenses/             openfortivpn's and OpenSSL's licences
#   artifacts/openfortivpn/<version>/sources/              the exact source archives (GPL: published with each release)
# The app build (OpenFortiClient.App.csproj, BundleOpenFortiVpn) runs this when the output is missing.
# Needs: brew install autoconf automake pkgconf. See docs/development.md, "Bundled openfortivpn".
set -eu

OPENFORTIVPN_VERSION=1.24.1
OPENFORTIVPN_SHA256=c40d33acd97b89c2e943bfd839c19b69e5a7a5997052e2fc9a595602745c0465
OPENSSL_VERSION=3.5.9
OPENSSL_SHA256=603f5602e2eef00d77fbd429d34dcd5822bb301757a1bc9cdb24c670f1eb859a
MACOS_MIN=14.0

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/openfortivpn/$OPENFORTIVPN_VERSION"
if [ -x "$OUT/openfortivpn" ] && [ "${1:-}" != "--force" ]; then
    echo "$OUT/openfortivpn is already built (--force to rebuild)."
    exit 0
fi

for tool in autoreconf automake pkgconf; do
    command -v "$tool" >/dev/null || { echo "$tool is missing: brew install autoconf automake pkgconf" >&2; exit 1; }
done

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
export MACOSX_DEPLOYMENT_TARGET="$MACOS_MIN"
JOBS="$(sysctl -n hw.ncpu)"
LOG="$WORK/build.log"
quiet() { "$@" >>"$LOG" 2>&1 || { tail -n 40 "$LOG" >&2; exit 1; }; }

fetch() { # url sha256 file
    curl -fsSL --retry 3 -o "$WORK/$3" "$1"
    echo "$2  $WORK/$3" | shasum -a 256 -c - >/dev/null || { echo "Checksum mismatch for $1" >&2; exit 1; }
}

fetch "https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/openssl-$OPENSSL_VERSION.tar.gz" \
    "$OPENSSL_SHA256" openssl.tar.gz
fetch "https://github.com/adrienverge/openfortivpn/archive/refs/tags/v$OPENFORTIVPN_VERSION.tar.gz" \
    "$OPENFORTIVPN_SHA256" openfortivpn.tar.gz
tar -xzf "$WORK/openssl.tar.gz" -C "$WORK"
tar -xzf "$WORK/openfortivpn.tar.gz" -C "$WORK"

# Static libraries only, so the binary depends on nothing outside macOS. OPENSSLDIR is macOS's own
# /etc/ssl: the default CA bundle is then /etc/ssl/cert.pem, which Apple maintains and only root can change.
echo "Building OpenSSL ${OPENSSL_VERSION}…"
cd "$WORK/openssl-$OPENSSL_VERSION"
quiet ./Configure darwin64-arm64-cc no-shared no-module no-tests no-docs no-apps \
    --prefix="$WORK/openssl" --openssldir=/private/etc/ssl "-mmacosx-version-min=$MACOS_MIN"
quiet make -j"$JOBS"
quiet make install_sw

echo "Building openfortivpn ${OPENFORTIVPN_VERSION}…"
cd "$WORK/openfortivpn-$OPENFORTIVPN_VERSION"
quiet ./autogen.sh
# As Homebrew builds it (legacy pppd: macOS ships pppd 2.4), but against the static OpenSSL above.
quiet env PKG_CONFIG_PATH="$WORK/openssl/lib/pkgconfig" PKG_CONFIG="pkgconf --static" ./configure \
    --enable-legacy-pppd --prefix=/usr/local --sysconfdir=/etc \
    CFLAGS="-O2 -mmacosx-version-min=$MACOS_MIN"
quiet make -j"$JOBS"

if otool -L openfortivpn | tail -n +2 | grep -v -E '^\s+/(usr/lib|System/Library)/'; then
    echo "openfortivpn links a library outside macOS (above); it would not run on another Mac." >&2
    exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT/licenses" "$OUT/sources"
cp openfortivpn "$OUT/openfortivpn"
cp LICENSE "$OUT/licenses/openfortivpn-LICENSE.txt"
cp LICENSE.OpenSSL "$OUT/licenses/openfortivpn-LICENSE.OpenSSL.txt"
cp "$WORK/openssl-$OPENSSL_VERSION/LICENSE.txt" "$OUT/licenses/OpenSSL-LICENSE.txt"
cp "$WORK/openfortivpn.tar.gz" "$OUT/sources/openfortivpn-$OPENFORTIVPN_VERSION.tar.gz"
cp "$WORK/openssl.tar.gz" "$OUT/sources/openssl-$OPENSSL_VERSION.tar.gz"
"$OUT/openfortivpn" --version
echo "Built $OUT/openfortivpn"
