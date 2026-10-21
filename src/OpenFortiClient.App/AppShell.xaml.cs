using OpenFortiClient.App.Views;

namespace OpenFortiClient.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(ConnectionEditPage), typeof(ConnectionEditPage));
        Routing.RegisterRoute(nameof(SystemVpnEditPage), typeof(SystemVpnEditPage));
    }
}
