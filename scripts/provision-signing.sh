#!/bin/sh
# Sets up the signing that a build needs and writes src/OpenFortiClient.App/Signing.local.props.
#
#   ./scripts/provision-signing.sh <TEAM_ID>                 Apple Development: this Mac only, for development.
#       Creates or renews the certificate and profile. Sign in to Xcode first (Settings > Accounts).
#       See docs/development.md, "Code signing".
#   ./scripts/provision-signing.sh <TEAM_ID> --developer-id  Developer ID: for releases (paid account).
#       Uses the Developer ID certificate and profile that docs/release.md has you create once.
set -eu

TEAM_ID="${1:?usage: $0 <TEAM_ID> [--developer-id]   (find it with: defaults read com.apple.dt.Xcode IDEProvisioningTeamByIdentifier)}"
BUNDLE_ID="io.github.openforticlient"
PROFILE_NAME="Mac Team Provisioning Profile: $BUNDLE_ID"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

write_props() { # identity profile
    # codesign cannot find an identity by a name with non-ASCII characters (such as "Lóránt Kovács"),
    # so sign by the certificate's SHA-1 hash, which codesign and the .NET SDK both accept.
    HASH="$(security find-identity -v -p codesigning | grep -F "\"$1\"" | awk '{ print $2 }' | head -n 1)"
    cat > "$ROOT/src/OpenFortiClient.App/Signing.local.props" <<PROPS
<Project>
  <!-- Written by scripts/provision-signing.sh; not committed. CodesignKey: $1 -->
  <PropertyGroup>
    <CodesignKey>$HASH</CodesignKey>
    <CodesignProvision>$2</CodesignProvision>
    <DevelopmentTeam>$TEAM_ID</DevelopmentTeam>
  </PropertyGroup>
</Project>
PROPS
    echo "Signing set up with \"$1\" and \"$2\"."
}

if [ "${2:-}" = "--developer-id" ]; then
    IDENTITY="$(security find-identity -v -p codesigning | sed -n "s/.*\"\(Developer ID Application: [^\"]*($TEAM_ID)\)\".*/\1/p" | head -n 1)"
    if [ -z "$IDENTITY" ]; then
        echo "No Developer ID Application certificate for team $TEAM_ID in the keychain. Create it in Xcode:" >&2
        echo "Settings > Accounts > (team) > Manage Certificates > + > Developer ID Application." >&2
        exit 1
    fi

    # A Developer ID profile runs on any Mac (ProvisionsAllDevices) and grants the Keychain group.
    PROFILE=""
    for file in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles/"*.provisionprofile \
                "$HOME/Library/MobileDevice/Provisioning Profiles/"*.provisionprofile; do
        [ -f "$file" ] || continue
        plist="$(security cms -D -i "$file" 2>/dev/null)" || continue
        [ "$(echo "$plist" | plutil -extract 'Entitlements.com\.apple\.application-identifier' raw - 2>/dev/null)" = "$TEAM_ID.$BUNDLE_ID" ] || continue
        [ "$(echo "$plist" | plutil -extract ProvisionsAllDevices raw - 2>/dev/null)" = "true" ] || continue
        expires="$(echo "$plist" | plutil -extract ExpirationDate raw -)"
        [ "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \< "$expires" ] || continue
        PROFILE="$(echo "$plist" | plutil -extract Name raw -)"
        break
    done
    if [ -z "$PROFILE" ]; then
        echo "No valid Developer ID provisioning profile for $TEAM_ID.$BUNDLE_ID. Create one at" >&2
        echo "developer.apple.com > Certificates, Identifiers & Profiles > Profiles > + > Developer ID," >&2
        echo "for the App ID $BUNDLE_ID and your Developer ID certificate, then double-click the download." >&2
        exit 1
    fi

    write_props "$IDENTITY" "$PROFILE"
    echo "Build the release with: ./scripts/build-release.sh && ./scripts/notarize.sh"
    exit 0
fi
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

write_props "$IDENTITY" "$PROFILE_NAME"
echo "Rebuild with: dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst --no-incremental"
