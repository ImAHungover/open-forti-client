# Third-party notices

OpenFortiClient is a [ResultLab](https://resultlab.io/products) open-source product, MIT licensed (see [LICENSE](LICENSE)). It uses the following software.

| Component | Licence | How it is used |
|---|---|---|
| [openfortivpn](https://github.com/adrienverge/openfortivpn) | GPLv3 | **Not bundled.** Installed separately (`brew install openfortivpn`) and run as a separate program by the privileged helper (or by `sudo` in development builds). OpenFortiClient does not link against it. |
| [.NET MAUI](https://github.com/dotnet/maui) and the .NET runtime | MIT | Linked into the app. |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | Linked into the app. |
| [Microsoft.Extensions.Logging.Debug](https://github.com/dotnet/runtime) | MIT | Linked into the app. |
| [Inter](https://github.com/rsms/inter) font | SIL Open Font License 1.1 ([licence](third-party/fonts/Inter-LICENSE.txt)) | Bundled in the app. |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) font | SIL Open Font License 1.1 ([licence](third-party/fonts/JetBrainsMono-OFL.txt)) | Bundled in the app. |

Test-only tools (xUnit, Appium, Appium.WebDriver) are not part of the app.

FortiClient, FortiGate and Fortinet are trademarks of Fortinet, Inc. This project is not affiliated with or endorsed by Fortinet.
