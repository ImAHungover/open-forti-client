using System.Text.Json.Nodes;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Mac;

namespace OpenFortiClient.App.UiTests;

/// <summary>
/// One launch of the end-to-end build of the app (bundle io.github.openforticlient.e2e) against its
/// own scratch folder: seeded profiles and settings, a scripted openfortivpn, no Keychain, no root.
/// </summary>
public sealed class AppSession : IDisposable
{
    public const string ProfileId = "11111111-1111-1111-1111-111111111111";
    public const string Gateway1 = "vpn1.example.com";
    public const string Gateway2 = "vpn2.example.com";

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(15);

    private AppSession(string directory, MacDriver driver)
    {
        Directory = directory;
        Driver = driver;
    }

    public string Directory { get; }

    public MacDriver Driver { get; }

    /// <summary>Gateway hosts in the order the app started openfortivpn for them.</summary>
    public IReadOnlyList<string> StartedGateways => ReadLines("started.log");

    /// <summary>Lines the app wrote to openfortivpn's stdin (OTPs).</summary>
    public IReadOnlyList<string> Input => ReadLines("input.log");

    /// <summary>First line of the client key each start received, as "start line".</summary>
    public IReadOnlyList<string> ClientKeysPassed => ReadLines("client-key.log");

    /// <summary>Certificates each openfortivpn start was told to trust, as "start digest".</summary>
    public IReadOnlyList<string> TrustedCertsPassed => ReadLines("trusted.log");

    /// <summary>macOS banners the app showed, as "severity|title|body".</summary>
    public IReadOnlyList<string> SystemNotifications => ReadLines("system-notifications.log");

    /// <summary>The saved passwords (the test build's stand-in for the Keychain), as "key\tvalue".</summary>
    public IReadOnlyList<string> Secrets => ReadLines("secrets.txt");

    /// <summary>The app's own log (app.log in the test folder).</summary>
    public string AppLog
    {
        get
        {
            var path = Path.Combine(Directory, "logs", "app.log");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
    }

    public string ProfilesJson => File.ReadAllText(Path.Combine(Directory, "data", "profiles.json"));

    /// <param name="withProfile">Seed one connection, "Test VPN", with two gateways.</param>
    /// <param name="settings">Changes to the seeded settings.json.</param>
    /// <param name="profile">Changes to the seeded profile.</param>
    public static AppSession Start(
        Scenario scenario,
        bool withProfile = true,
        string? savedPassword = null,
        Action<JsonObject>? settings = null,
        Action<JsonObject>? profile = null,
        Action<string>? beforeLaunch = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "openforticlient-e2e", Guid.NewGuid().ToString("N"));
        var data = Path.Combine(directory, "data");
        System.IO.Directory.CreateDirectory(data);

        // The engine checks that the openfortivpn binary exists; the scripted launcher never runs it.
        var fakeBinary = Path.Combine(directory, "openfortivpn");
        File.WriteAllText(fakeBinary, "");
        var settingsJson = new JsonObject
        {
            ["openFortiVpnPath"] = fakeBinary,
            ["gatewayTimeoutSeconds"] = 3,
        };
        settings?.Invoke(settingsJson);
        File.WriteAllText(Path.Combine(data, "settings.json"), settingsJson.ToJsonString());

        if (withProfile)
        {
            var profileJson = new JsonObject
            {
                ["id"] = ProfileId,
                ["name"] = "Test VPN",
                ["gateways"] = new JsonArray($"https://{Gateway1}:443/", $"https://{Gateway2}:443/"),
                ["authMode"] = savedPassword is null ? "PromptOnLogin" : "SaveLogin",
                ["username"] = "jane",
                ["trustedCertDigests"] = new JsonArray(),
            };
            profile?.Invoke(profileJson);
            File.WriteAllText(Path.Combine(data, "profiles.json"), new JsonArray(profileJson).ToJsonString());
        }

        if (savedPassword is not null)
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(savedPassword));
            File.WriteAllText(Path.Combine(directory, "secrets.txt"), $"profile/{ProfileId.Replace("-", "")}/password\t{encoded}\n");
        }

        File.WriteAllText(Path.Combine(directory, "scenario.json"), scenario.ToJson());
        beforeLaunch?.Invoke(directory);

        // A test copy left running (Debug or Release, same bundle ID) would be activated instead of launched.
        KillTestCopies();

        var options = new AppiumOptions
        {
            PlatformName = "mac",
            AutomationName = "mac2",
        };
        options.AddAdditionalAppiumOption("appPath", AppBundle());
        options.AddAdditionalAppiumOption("environment", new Dictionary<string, string> { ["OPENFORTICLIENT_E2E_DIR"] = directory });

        // Appium's helper (WebDriverAgentMac) now and then dies while a session starts; one retry covers that.
        MacDriver driver;
        try
        {
            driver = new MacDriver(AppiumServer.Url, options, TimeSpan.FromMinutes(3));
        }
        catch (WebDriverException ex) when (ex.Message.Contains("cannot be proxied", StringComparison.Ordinal))
        {
            driver = new MacDriver(AppiumServer.Url, options, TimeSpan.FromMinutes(3));
        }

        return new AppSession(directory, driver);
    }

    /// <summary>Releases a scripted <c>WaitFor(name)</c> step, for example to drop the tunnel.</summary>
    public void Trigger(string name) => File.WriteAllText(Path.Combine(Directory, name), "");

    // Every Appium call costs 0.3 to 1.5 s on macOS (XCTest waits for the app to be idle), so the helpers
    // make as few as they can: no "displayed" check per lookup, one attribute per text read.

    /// <summary>An element by AutomationId, waiting until it exists.</summary>
    public AppiumElement Element(string automationId, TimeSpan? timeout = null) =>
        WaitFor(() => Driver.FindElements(MobileBy.AccessibilityId(automationId)).FirstOrDefault(), timeout)
            ?? throw Fail($"No element with AutomationId '{automationId}'.");

    /// <summary>The texts of all elements with this AutomationId, in screen order (for example list rows).</summary>
    public IReadOnlyList<string> Texts(string automationId) =>
        Driver.FindElements(MobileBy.AccessibilityId(automationId)).Select(TextOf).ToList();

    public bool IsVisible(string automationId) =>
        Driver.FindElements(MobileBy.AccessibilityId(automationId)).Any(e => e.Displayed);

    /// <summary>A button by its title, such as a tab ("Settings"). Dialog buttons: <see cref="DialogButton"/>.</summary>
    public AppiumElement Titled(string title, TimeSpan? timeout = null)
    {
        // XCUIElementType 9 = button: the tab bar's tabs ("Settings", "Notifications", …) are buttons.
        var predicate = $"(title == '{title}' OR label == '{title}') AND elementType == 9";
        return WaitFor(() => Driver.FindElements(MobileBy.IosNSPredicate(predicate)).FirstOrDefault(), timeout)
            ?? throw Fail($"No visible button or tab titled '{title}'.");
    }

    public void Tap(string automationId) => Element(automationId).Click();

    /// <summary>
    /// A button of the dialog that is showing. On Mac Catalyst an alert is a sheet on the window; the
    /// accessibility tree also holds hidden copies of its buttons, so look inside the sheet only.
    /// </summary>
    public AppiumElement DialogButton(string title, TimeSpan? timeout = null) =>
        WaitFor(() => Driver.FindElements(MobileBy.IosClassChain($"**/XCUIElementTypeSheet/**/XCUIElementTypeButton[`title == '{title}' OR label == '{title}'`]"))
                .FirstOrDefault(), timeout)
            ?? throw Fail($"No dialog with a '{title}' button appeared.");

    /// <summary>
    /// Opens the app's menu bar icon and returns the menu entry with this title. The icon is found by
    /// the accessibility identifier the Swift code gives its button.
    /// </summary>
    public AppiumElement MenuBarItem(string title, TimeSpan? timeout = null)
    {
        var icon = WaitFor(() => Driver.FindElements(MobileBy.AccessibilityId("OpenFortiClientStatusItem")).FirstOrDefault(), timeout)
            ?? throw Fail("The menu bar icon is not there.");
        icon.Click();
        return WaitFor(() => Driver.FindElements(MobileBy.IosClassChain($"**/XCUIElementTypeMenuItem[`title == '{title}'`]")).FirstOrDefault(e => e.Displayed), timeout)
            ?? throw Fail($"The menu bar menu has no '{title}' entry.");
    }

    /// <summary>All the text of the dialog that is showing (its title and message).</summary>
    public string DialogText(TimeSpan? timeout = null)
    {
        var texts = WaitFor(() =>
        {
            var found = Driver.FindElements(MobileBy.IosClassChain("**/XCUIElementTypeSheet/**/XCUIElementTypeStaticText")).Select(TextOf).ToList();
            return found.Count > 0 ? found : null;
        }, timeout) ?? throw Fail("No dialog with text appeared.");
        return string.Join("\n", texts);
    }

    /// <summary>Waits until <paramref name="read"/> returns at least <paramref name="count"/> lines.</summary>
    public static IReadOnlyList<string> WaitForLines(Func<IReadOnlyList<string>> read, int count, TimeSpan? timeout = null) =>
        WaitFor(() => read() is { } lines && lines.Count >= count ? lines : null, timeout) ?? read();

    /// <summary>Types into the text field of the dialog that is showing (a prompt such as the OTP).</summary>
    public void TypeIntoAlert(string text)
    {
        var field = WaitFor(() => Driver.FindElements(MobileBy.IosClassChain("**/XCUIElementTypeSheet/**/XCUIElementTypeTextField")).FirstOrDefault())
            ?? throw Fail("No alert with a text field appeared.");
        field.SendKeys(text);
    }

    /// <param name="replace">Clear the field first (1.3 s); only needed when it already holds text.</param>
    public void Type(string automationId, string text, bool replace = false)
    {
        var field = Element(automationId);
        if (replace)
        {
            field.Clear();
        }

        field.SendKeys(text);
    }

    /// <summary>Waits until the element's text contains <paramref name="expected"/>, and returns the text.</summary>
    public string WaitForText(string automationId, string expected, TimeSpan? timeout = null)
    {
        var last = "";
        var found = WaitFor(() =>
        {
            last = TextOf(Element(automationId, timeout));
            return last.Contains(expected, StringComparison.Ordinal) ? last : null;
        }, timeout);
        return found ?? throw Fail($"'{automationId}' never showed '{expected}'; last text was '{last}'.");
    }

    /// <summary>A label's text is its "label", an entry's its "value", a button's its "title": read in that order, stopping early.</summary>
    public static string TextOf(AppiumElement element)
    {
        foreach (var attribute in new[] { "label", "value", "title" })
        {
            if (element.GetAttribute(attribute) is { Length: > 0 } text)
            {
                return text;
            }
        }

        return "";
    }

    public static T? WaitFor<T>(Func<T?> probe, TimeSpan? timeout = null)
        where T : class
    {
        var until = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (true)
        {
            try
            {
                if (probe() is { } result)
                {
                    return result;
                }
            }
            catch (WebDriverException)
            {
                // The element went away while we looked at it; look again.
            }
            catch (TimeoutException)
            {
                // A nested wait gave up; the outer one decides.
            }

            if (DateTime.UtcNow > until)
            {
                return null;
            }

            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        try
        {
            Driver.Quit();
        }
        catch (WebDriverException)
        {
            // The app may already have quit (the Quit test).
        }
        finally
        {
            Driver.Dispose();
            KillTestCopies();
            try
            {
                // OPENFORTICLIENT_E2E_KEEP=1 keeps the folder (app log, profiles, scenario) for debugging.
                if (Environment.GetEnvironmentVariable("OPENFORTICLIENT_E2E_KEEP") != "1")
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // The app may still be writing its log; the folder is under the temp directory anyway.
            }
        }
    }

    /// <summary>
    /// A screenshot of the app's window only (never the rest of the screen), to
    /// artifacts/e2e.noindex/screens/&lt;name&gt;.png, for looking at the layout.
    /// </summary>
    public void SaveScreenshot(string name)
    {
        var folder = Path.Combine(RepositoryRoot(), "artifacts", "e2e.noindex", "screens");
        System.IO.Directory.CreateDirectory(folder);
        var window = Driver.FindElement(MobileBy.AccessibilityId("SceneWindow"));
        window.GetScreenshot().SaveAsFile(Path.Combine(folder, $"{name}.png"));
    }

    /// <summary>Saves what was on screen (the accessibility tree) next to the test results, for the failure message.</summary>
    private TimeoutException Fail(string message)
    {
        try
        {
            var folder = Path.Combine(RepositoryRoot(), "artifacts", "e2e.noindex", "failures");
            System.IO.Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{DateTime.Now:HHmmss-fff}.xml");
            File.WriteAllText(file, Driver.PageSource);
            return new TimeoutException($"{message} Screen saved to {file}");
        }
        catch (WebDriverException)
        {
            return new TimeoutException(message);
        }
    }

    /// <summary>Stops leftover test copies of the app. Matches only "OpenFortiClient E2E.app", never the real app.</summary>
    private static void KillTestCopies()
    {
        using var pkill = System.Diagnostics.Process.Start("/usr/bin/pkill", ["-f", "OpenFortiClient E2E.app/Contents/MacOS/"]);
        pkill?.WaitForExit();
        Thread.Sleep(300);
    }

    private IReadOnlyList<string> ReadLines(string file)
    {
        var path = Path.Combine(Directory, file);
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "OpenFortiClient.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Repository root not found.");
        }

        return root;
    }

    private static string AppBundle()
    {
        // Debug, or Release with ./scripts/run-ui-tests.sh --release (catches trimming problems).
        var configuration = Environment.GetEnvironmentVariable("OPENFORTICLIENT_E2E_CONFIGURATION") is { Length: > 0 } c ? c : "Debug";
        var bundle = Path.Combine(RepositoryRoot(), "artifacts", "e2e.noindex", "bin", configuration, "net10.0-maccatalyst", "maccatalyst-arm64", "OpenFortiClient E2E.app");
        return System.IO.Directory.Exists(bundle)
            ? bundle
            : throw new InvalidOperationException($"{bundle} is missing. Build it with ./scripts/run-ui-tests.sh, or: dotnet build src/OpenFortiClient.App -f net10.0-maccatalyst -p:E2E=true");
    }
}
