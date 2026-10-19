#!/bin/sh
# Creates or renews the Apple Development certificate and provisioning profile that a signed
# build needs, then writes src/OpenFortiClient.App/Signing.local.props. See docs/development.md.
#
# Sign in to Xcode first (Settings > Accounts), then run: ./scripts/provision-signing.sh <TEAM_ID>
set -eu

TEAM_ID="${1:?usage: $0 <TEAM_ID>   (find it with: defaults read com.apple.dt.Xcode IDEProvisioningTeamByIdentifier)}"
BUNDLE_ID="io.github.openforticlient"
PROFILE_NAME="Mac Team Provisioning Profile: $BUNDLE_ID"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DERIVED_DATA="$(mktemp -d)"
trap 'rm -rf "$DERIVED_DATA"' EXIT

# Building the stub app with automatic signing makes Xcode register the bundle ID and this Mac,
# create the certificate if needed, and download a profile that grants keychain-access-groups.
# The destination must be this Mac, not generic/platform=macOS: only then does Xcode add this Mac
# to the profile, and macOS refuses to launch the app on a Mac the profile does not list.
xcodebuild -quiet build \
    -project "$ROOT/scripts/provisioning/Provision.xcodeproj" \
    -scheme Provision \
    -destination 'platform=macOS,arch=arm64' \
    -derivedDataPath "$DERIVED_DATA" \
    -allowProvisioningUpdates \
    DEVELOPMENT_TEAM="$TEAM_ID" \
    PRODUCT_BUNDLE_IDENTIFIER="$BUNDLE_ID"

IDENTITY="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Development: [^"]*\)".*/\1/p' | head -n 1)"
if [ -z "$IDENTITY" ]; then
    echo "No Apple Development certificate found after provisioning." >&2
    if security find-identity -p codesigning | grep -q "Apple Development"; then
        echo "A certificate exists but is not valid. Usually the Apple WWDR G3 intermediate is missing:" >&2
        echo "download https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer and open it to add it to the login keychain." >&2
    fi
    exit 1
fi

cat > "$ROOT/src/OpenFortiClient.App/Signing.local.props" <<PROPS
<Project>
  <!-- Written by scripts/provision-signing.sh; not committed. -->
  <PropertyGroup>
    <CodesignKey>$IDENTITY</CodesignKey>
    <CodesignProvision>$PROFILE_NAME</CodesignProvision>
    <DevelopmentTeam>$TEAM_ID</DevelopmentTeam>
  </PropertyGroup>
</Project>
PROPS

echo "Signing set up with \"$IDENTITY\" and \"$PROFILE_NAME\"."
echo "Rebuild with: dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst --no-incremental"
