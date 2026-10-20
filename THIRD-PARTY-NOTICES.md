# Third-party notices

OpenFortiClient is a [ResultLab](https://resultlab.io/products) open-source product, MIT licensed (see [LICENSE](LICENSE)). It uses the following software.

| Component | Licence | How it is used |
|---|---|---|
| [openfortivpn](https://github.com/adrienverge/openfortivpn) 1.24.1 | GPLv3, with an exception for linking OpenSSL | **Bundled, unmodified**, as `Contents/MacOS/openfortivpn`, built by [scripts/build-openfortivpn.sh](scripts/build-openfortivpn.sh). Run as a separate program by the privileged helper; OpenFortiClient does not link against it. Licence texts in `Contents/Resources/Licenses`. |
| [OpenSSL](https://www.openssl.org) 3.5.9 | Apache 2.0 | Statically linked into the bundled openfortivpn, unmodified. |
| [.NET MAUI](https://github.com/dotnet/maui) and the .NET runtime | MIT | Linked into the app. |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | Linked into the app. |
| [Microsoft.Extensions.Logging.Debug](https://github.com/dotnet/runtime) | MIT | Linked into the app. |
| [Inter](https://github.com/rsms/inter) font | SIL Open Font License 1.1 ([licence](third-party/fonts/Inter-LICENSE.txt)) | Bundled in the app. |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) font | SIL Open Font License 1.1 ([licence](third-party/fonts/JetBrainsMono-OFL.txt)) | Bundled in the app. |

**Source code of the bundled openfortivpn and OpenSSL.** Each [GitHub release](https://github.com/ImAHungover/open-forti-client/releases) carries the exact source archives the DMG was built from (`openfortivpn-<version>.tar.gz`, `openssl-<version>.tar.gz`), and the build script and its settings are in this repository at the release's tag. Development builds of the app (sudo mode) use a separately installed openfortivpn instead.

Test-only tools (xUnit, Appium, Appium.WebDriver) are not part of the app.

FortiClient, FortiGate and Fortinet are trademarks of Fortinet, Inc. This project is not affiliated with or endorsed by Fortinet.
