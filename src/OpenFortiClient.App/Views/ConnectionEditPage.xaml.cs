using OpenFortiClient.App.ViewModels;

namespace OpenFortiClient.App.Views;

public partial class ConnectionEditPage : ContentPage
{
    public ConnectionEditPage(ConnectionEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
