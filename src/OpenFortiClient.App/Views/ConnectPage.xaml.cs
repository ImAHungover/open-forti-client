using OpenFortiClient.App.ViewModels;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.App.Views;

public partial class ConnectPage : ContentPage
{
    private readonly ConnectViewModel _viewModel;

    public ConnectPage(ConnectViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Set here so the XAML's compiled bindings (typed to the view model) don't try to resolve Name.
        ProfilePicker.ItemDisplayBinding = new Binding(nameof(VpnProfile.Name));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
