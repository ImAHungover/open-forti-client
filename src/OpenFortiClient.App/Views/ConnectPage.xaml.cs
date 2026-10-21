using OpenFortiClient.App.ViewModels;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.App.Views;

public partial class ConnectPage : ContentPage
{
    private readonly ConnectViewModel _viewModel;
    private readonly SystemVpnsViewModel _systemVpns;

    public ConnectPage(ConnectViewModel viewModel, SystemVpnsViewModel systemVpns)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        SystemVpnCard.BindingContext = _systemVpns = systemVpns;

        // Set here so the XAML's compiled bindings (typed to the view model) don't try to resolve Name.
        ProfilePicker.ItemDisplayBinding = new Binding(nameof(VpnProfile.Name));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        _systemVpns.Refresh();
    }
}
