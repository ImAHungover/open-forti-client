using OpenFortiClient.App.ViewModels;

namespace OpenFortiClient.App.Views;

public partial class SystemVpnEditPage : ContentPage
{
    public SystemVpnEditPage(SystemVpnEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
