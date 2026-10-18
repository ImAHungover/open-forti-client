# Release (phase 4b: going live)

How to publish OpenFortiClient officially: a **signed and notarised DMG** that anyone can download and open without warnings, and a **Homebrew cask** (`brew install --cask openforticlient`). This is the last phase of the roadmap ([README](../README.md#roadmap)). Version 1.0.0 today is a local build only: it is signed with a free Personal Team, is not notarised, and its signing profile expires after 7 days.

Status: **not started**. Items marked **TBD** are decisions or values that do not exist yet.

## Why this is a separate phase

| Need | Free Personal Team (today) | Paid Apple Developer Program |
|---|---|---|
| Signing identity | Apple Development (local testing only) | **Developer ID Application** (distribution outside the App Store) |
| Notarisation (`notarytool`) | Not available | Available: Gatekeeper opens the app without warnings |
| Provisioning profile for the Keychain group | 7 days, this Mac only | Developer ID profile, valid for years, any Mac |
| Privileged helper (`SMAppService`) on other Macs | Only on the Mac that built it | Works on any Mac |
| Homebrew cask | Not accepted unsigned or unnotarised (official tap); a private tap would still show Gatekeeper warnings | Accepted |

The paid membership costs 99 USD a year, and the account holder must be the publisher: ResultLab (**TBD**: the ResultLab Apple Developer account and team ID).

## Prerequisites (one time)

| What | How | Owner |
|---|---|---|
| Apple Developer Program membership for ResultLab | developer.apple.com/programs, organisation enrolment (needs a D-U-N-S number) | **TBD** |
| Developer ID Application certificate | Xcode → Settings → Accounts → Manage Certificates → + → Developer ID Application. Back it up as a `.p12` | **TBD** |
| App ID `io.github.openforticlient` with the Keychain Sharing capability | developer.apple.com → Identifiers. Consider a ResultLab bundle ID instead (for example `io.resultlab.openforticlient`, **TBC**); changing it later loses saved passwords and the helper registration | **TBD** |
| Developer ID provisioning profile for that App ID | developer.apple.com → Profiles → Developer ID. It grants `keychain-access-groups` for distribution | **TBD** |
| Notarisation credentials in the keychain | `xcrun notarytool store-credentials openforticlient-notary --apple-id <id> --team-id <TEAM> --password <app-specific password>` (or an App Store Connect API key) | **TBD** |
| Public GitHub repository for the source and releases | for example `github.com/resultlab/open-forti-client` (**TBD**) | **TBD** |
| Homebrew tap repository | `github.com/resultlab/homebrew-tap` (**TBD**); later, optionally, the official `homebrew/cask` | **TBD** |

## Changes to make in the code (once)

1. **Signing for distribution.** In `src/OpenFortiClient.App/Signing.local.props` (local) or as CI secrets:
   ```xml
   <CodesignKey>Developer ID Application: ResultLab (TEAMID)</CodesignKey>
   <CodesignProvision>OpenFortiClient Developer ID</CodesignProvision>
   <DevelopmentTeam>TEAMID</DevelopmentTeam>
   <UseHardenedRuntime>true</UseHardenedRuntime>
   ```
   The helper is already signed with hardened runtime (`BuildHelper` target). Notarisation also needs a secure timestamp: change its `codesign --timestamp=none` to `--timestamp` for release builds.
2. **Hardened runtime for the app.** Notarisation requires it. Check that .NET's runtime still starts under it; it usually needs `com.apple.security.cs.allow-jit` in `Entitlements.Signed.plist`. Test this before the first submission.
3. **DMG creation.** `hdiutil create` still works but is deprecated. Move `scripts/build-release.sh` to `diskutil image create` (or `create-dmg` for a background image and an icon layout). Sign the DMG itself: `codesign --sign "Developer ID Application: …" --timestamp OpenFortiClient-x.y.z.dmg`.
4. **Notarise and staple** (new script, for example `scripts/notarize.sh`):
   ```sh
   xcrun notarytool submit artifacts/release/OpenFortiClient-$VERSION.dmg \
       --keychain-profile openforticlient-notary --wait
   xcrun stapler staple artifacts/release/OpenFortiClient-$VERSION.dmg
   xcrun stapler validate artifacts/release/OpenFortiClient-$VERSION.dmg
   spctl --assess --type open --context context:primary-signature -vv artifacts/release/OpenFortiClient-$VERSION.dmg
   ```
   If it is rejected, `xcrun notarytool log <submission-id> --keychain-profile openforticlient-notary` lists every problem (unsigned binary, missing timestamp, missing hardened runtime).
5. **Universal build (optional, TBC).** Release builds are Apple Silicon only. For Intel Macs, build the Swift parts for `x86_64` too (`lipo` both), and drop `-r maccatalyst-arm64` from the publish.
6. **Bundle a verified openfortivpn (recommended, TBC).** This removes the Homebrew dependency and closes the documented risk that the Homebrew binary is writable by the admin user ([architecture.md](architecture.md#risks)). It means building openfortivpn and its OpenSSL from source in CI, signing them with the Developer ID, and pointing the helper's allowlist at the bundled path only. openfortivpn is GPLv3: shipping its binary means also offering its exact source (a link to the tagged source and the build script in the release) and including its licence. The app's own code stays MIT, because it runs openfortivpn as a separate program.

## Release checklist (each version)

1. Update `ApplicationDisplayVersion` and `ApplicationVersion` in `src/OpenFortiClient.App/OpenFortiClient.App.csproj`, and note the changes in a `CHANGELOG.md` (**TBD**: create it at the first public release).
2. Run all tests: `dotnet test OpenFortiClient.Core.slnf`, `./scripts/test-helper.sh`, `./scripts/run-ui-tests.sh` and `./scripts/run-ui-tests.sh --release`.
3. Run `./scripts/build-release.sh` with the Developer ID signing, then `scripts/notarize.sh`.
4. On a **second, clean Mac** (or a fresh user account): download the DMG from a browser (so it gets quarantined), open it, drag the app to Applications, start it, install and approve the helper, and connect. Gatekeeper must not warn.
5. Tag the source (`git tag v1.0.0`) and create a GitHub release with the DMG, its SHA-256 (`shasum -a 256`) and the changelog.
6. Update the Homebrew cask (below) with the new version and SHA-256, and test it: `brew install --cask resultlab/tap/openforticlient`.

## Homebrew cask

The first home is ResultLab's own tap (`resultlab/homebrew-tap`, **TBD**). A cask for `homebrew/cask` can follow once the project meets its notability rules. The cask installs the notarised DMG, requires openfortivpn (unless it is bundled, step 6 above), and removes the helper on uninstall:

```ruby
cask "openforticlient" do
  version "1.0.0"
  sha256 "TBD"   # shasum -a 256 OpenFortiClient-1.0.0.dmg

  url "https://github.com/resultlab/open-forti-client/releases/download/v#{version}/OpenFortiClient-#{version}.dmg"   # TBD repository
  name "OpenFortiClient"
  desc "Open-source client for Fortinet SSL-VPN gateways, a ResultLab product"
  homepage "https://resultlab.io/products"

  depends_on macos: ">= :sonoma"
  depends_on arch: :arm64
  depends_on formula: "openfortivpn"

  app "OpenFortiClient.app"

  uninstall quit:      "io.github.openforticlient",
            launchctl: "io.github.openforticlient.helper"

  zap trash: [
    "~/Library/Application Support/OpenFortiClient",
    "~/Library/Logs/OpenFortiClient",
  ]
end
```

After installing, the user still opens the app once and approves the helper under System Settings → General → Login Items. macOS does not allow an installer to do that for them.

## Automating it (TBC)

A GitHub Actions workflow on a `macos` runner, started by a version tag, can do the whole checklist:
1. Install Xcode, .NET 10 and the MAUI workload.
2. Import the Developer ID certificate and the provisioning profile from encrypted secrets into a temporary keychain.
3. Run the Core and helper tests (UI tests need a logged-in session with Accessibility permission, so they stay a manual step, or need a self-hosted runner).
4. Run `build-release.sh` and `notarize.sh`, upload the DMG to the release, and update the tap with the new SHA-256.

## Done when

- A DMG from the GitHub release opens on a clean Mac without any Gatekeeper warning.
- `spctl` accepts it, and `stapler validate` passes.
- `brew install --cask resultlab/tap/openforticlient` installs it and `brew uninstall --zap` removes it with the helper.
- The README's "Install" section points to the download and the cask instead of building it yourself.
