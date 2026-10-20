#!/bin/sh
# Writes the Homebrew cask for the release DMG (after scripts/notarize.sh, which changes its checksum) to
# artifacts/release/openforticlient.rb. Publish it as Casks/openforticlient.rb in the tap repository
# github.com/ImAHungover/homebrew-tap. See docs/release.md, "Homebrew cask".
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$ROOT/src/OpenFortiClient.App/OpenFortiClient.App.csproj")"
DMG="$ROOT/artifacts/release/OpenFortiClient-$VERSION.dmg"
[ -f "$DMG" ] || { echo "$DMG not found: run scripts/build-release.sh and scripts/notarize.sh first." >&2; exit 1; }
xcrun stapler validate -q "$DMG" 2>/dev/null || echo "Warning: $DMG is not notarised; Homebrew users would get a Gatekeeper warning." >&2
SHA256="$(shasum -a 256 "$DMG" | cut -d ' ' -f 1)"

cat > "$ROOT/artifacts/release/openforticlient.rb" <<CASK
cask "openforticlient" do
  version "$VERSION"
  sha256 "$SHA256"

  url "https://github.com/ImAHungover/open-forti-client/releases/download/v#{version}/OpenFortiClient-#{version}.dmg"
  name "OpenFortiClient"
  desc "Open-source client for Fortinet SSL-VPN gateways"
  homepage "https://github.com/ImAHungover/open-forti-client"

  depends_on arch: :arm64
  depends_on macos: :sonoma

  app "OpenFortiClient.app"

  uninstall launchctl: "io.github.openforticlient.helper",
            quit:      "io.github.openforticlient"

  zap trash: [
    "~/Library/Application Support/OpenFortiClient",
    "~/Library/Logs/OpenFortiClient",
    "~/Library/Preferences/io.github.openforticlient.plist",
    "~/Library/Saved Application State/io.github.openforticlient~iosmac.savedState",
  ]

  caveats <<~EOS
    OpenFortiClient needs its privileged helper to set up routes and DNS. Open the app, go to
    Settings → Privileged helper → Install, then allow it in System Settings → General → Login Items.
  EOS
end
CASK
echo "Wrote $ROOT/artifacts/release/openforticlient.rb"
