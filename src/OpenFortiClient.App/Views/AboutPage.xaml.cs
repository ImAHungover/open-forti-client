using OpenFortiClient.App.ViewModels;

namespace OpenFortiClient.App.Views;

public partial class AboutPage : ContentPage
{
    private readonly AboutViewModel _viewModel;

    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
