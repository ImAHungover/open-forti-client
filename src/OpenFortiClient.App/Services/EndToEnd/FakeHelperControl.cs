#if E2E
namespace OpenFortiClient.App.Services.EndToEnd;

/// <summary>
/// The privileged helper's status for the UI tests: Install puts it in "waiting for approval", and the test
/// approves it with AppSession.Trigger("helper-approve"), as the user would in System Settings.
/// </summary>
public sealed class FakeHelperControl(string directory) : IHelperControl
{
    private HelperStatus _status = HelperStatus.NotRegistered;

    public HelperStatus Status
    {
        get
        {
            var approve = Path.Combine(directory, "helper-approve");
            if (_status == HelperStatus.RequiresApproval && File.Exists(approve))
            {
                File.Delete(approve);
                _status = HelperStatus.Enabled;
            }

            return _status;
        }
    }

    public string? Register()
    {
        _status = HelperStatus.RequiresApproval;
        return null;
    }

    public string? Unregister()
    {
        _status = HelperStatus.NotRegistered;
        return null;
    }

    public void OpenLoginItemsSettings() => File.WriteAllText(Path.Combine(directory, "login-items-opened"), "");
}
#endif
