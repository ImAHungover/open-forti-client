#!/bin/sh
# Builds the IPsec spike tool (docs/ipsec-spike.md) into artifacts/ipsec-spike/:
#   ./spikes/ipsec-spike/build.sh <TEAM_ID>                 Apple Development signing
#   ./spikes/ipsec-spike/build.sh <TEAM_ID> --developer-id  Developer ID signing (archive and export)
# Xcode registers io.github.openforticlient.ipsec-spike with the Personal VPN capability and makes the
# profile itself (-allowProvisioningUpdates). Run the tool as <app>/Contents/MacOS/IPsecSpike.
set -eu

TEAM_ID="${1:?usage: $0 <TEAM_ID> [--developer-id]}"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../../artifacts/ipsec-spike"
mkdir -p "$OUT"

if [ "${2:-}" = "--developer-id" ]; then
    cat > "$OUT/ExportOptions.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key><string>developer-id</string>
	<key>signingStyle</key><string>automatic</string>
	<key>teamID</key><string>$TEAM_ID</string>
</dict>
</plist>
PLIST
    xcodebuild -quiet archive -project "$HERE/IPsecSpike.xcodeproj" -scheme IPsecSpike -configuration Debug \
        -archivePath "$OUT/IPsecSpike.xcarchive" -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM_ID"
    rm -rf "$OUT/developer-id"
    xcodebuild -quiet -exportArchive -archivePath "$OUT/IPsecSpike.xcarchive" -exportOptionsPlist "$OUT/ExportOptions.plist" \
        -exportPath "$OUT/developer-id" -allowProvisioningUpdates
    APP="$OUT/developer-id/IPsecSpike.app"
else
    xcodebuild -quiet build -project "$HERE/IPsecSpike.xcodeproj" -target IPsecSpike -configuration Debug \
        -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM_ID" SYMROOT="$OUT/build" OBJROOT="$OUT/obj"
    APP="$OUT/build/Debug/IPsecSpike.app"
fi
codesign -dvv "$APP" 2>&1 | grep '^Authority=' | head -n 1
echo "Run: \"$APP/Contents/MacOS/IPsecSpike\" save <config.json>"
