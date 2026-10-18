using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenFortiClient.App.ViewModels;

/// <summary>One "Remote Gateway" row on the edit screen.</summary>
public sealed partial class GatewayEntry(Action<GatewayEntry> remove) : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [RelayCommand]
    private void Remove() => remove(this);
}
