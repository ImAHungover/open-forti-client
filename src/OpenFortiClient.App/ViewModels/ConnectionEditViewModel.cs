using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenFortiClient.App.Services;
using OpenFortiClient.Core.Profiles;

namespace OpenFortiClient.App.ViewModels;

/// <summary>Add or edit a connection (flows.md: Add or Edit Server Details).</summary>
public sealed partial class ConnectionEditViewModel : ObservableObject, IQueryAttributable
{
    public const string ProfileIdKey = "id";

    private readonly IProfileStore _profiles;
    private readonly IDialogService _dialogs;
    private readonly VpnSession _session;
    private VpnProfile? _original;

    public ConnectionEditViewModel(IProfileStore profiles, IDialogService dialogs, VpnSession session)
    {
        _profiles = profiles;
        _dialogs = dialogs;
        _session = session;
        TrustedCerts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoTrustedCerts));

        // "Add" navigates without parameters, so start with one empty gateway row.
        AddGateway();
    }

    public ObservableCollection<GatewayEntry> Gateways { get; } = [];

    /// <summary>Gateway certificates the user chose to trust; removing one means being asked again.</summary>
    public ObservableCollection<TrustedCertEntry> TrustedCerts { get; } = [];

    public bool HasNoTrustedCerts => TrustedCerts.Count == 0;

    [ObservableProperty]
    public partial string Title { get; set; } = "New VPN Connection";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool SaveLogin { get; set; } = true;

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string ClientCertificatePath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyInKeychain))]
    public partial string ClientKeyPath { get; set; } = "";

    /// <summary>The key is in the Keychain; a new path replaces it on save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyInKeychain))]
    public partial bool KeyInKeychain { get; set; }

    public bool ShowKeyInKeychain => KeyInKeychain && string.IsNullOrWhiteSpace(ClientKeyPath);

    [ObservableProperty]
    public partial bool AutoConnect { get; set; }

    [ObservableProperty]
    public partial bool AlwaysUp { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = query.TryGetValue(ProfileIdKey, out var value) && value is Guid guid ? guid : (Guid?)null;
        query.Clear();
        _ = LoadAsync(id);
    }

    private async Task LoadAsync(Guid? id)
    {
        _original = id is null ? null : await _profiles.GetAsync(id.Value);
        Title = _original is null ? "New VPN Connection" : "Edit VPN Connection";
        Name = _original?.Name ?? "";
        Description = _original?.Description ?? "";
        SaveLogin = _original?.AuthMode != AuthMode.PromptOnLogin;
        Username = _original?.Username ?? "";
        ClientCertificatePath = _original?.ClientCertificatePath ?? "";
        ClientKeyPath = _original?.ClientKeyPath ?? "";
        KeyInKeychain = _original?.ClientKeyInKeychain ?? false;
        AutoConnect = _original?.AutoConnect ?? false;
        AlwaysUp = _original?.AlwaysUp ?? false;

        Gateways.Clear();
        foreach (var gateway in _original?.Gateways ?? [])
        {
            Gateways.Add(new GatewayEntry(RemoveGateway) { Text = gateway.ToString().TrimEnd('/') });
        }

        if (Gateways.Count == 0)
        {
            AddGateway();
        }

        TrustedCerts.Clear();
        foreach (var digest in _original?.TrustedCertDigests ?? [])
        {
            TrustedCerts.Add(new TrustedCertEntry(digest, entry => TrustedCerts.Remove(entry)));
        }
    }

    [RelayCommand]
    private async Task BrowseCertificateAsync() => ClientCertificatePath = await PickPemAsync("Choose the client certificate (PEM)") ?? ClientCertificatePath;

    [RelayCommand]
    private void RemoveKey()
    {
        KeyInKeychain = false;
        ClientKeyPath = "";
    }

    [RelayCommand]
    private async Task BrowseKeyAsync() => ClientKeyPath = await PickPemAsync("Choose the client key (PEM)") ?? ClientKeyPath;

    private async Task<string?> PickPemAsync(string title)
    {
        try
        {
            return (await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title }))?.FullPath;
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Could not open the file", ex.Message);
            return null;
        }
    }

    [RelayCommand]
    private void AddGateway() => Gateways.Add(new GatewayEntry(RemoveGateway));

    private void RemoveGateway(GatewayEntry entry) => Gateways.Remove(entry);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var gateways = new List<Uri>();
        foreach (var entry in Gateways.Where(g => !string.IsNullOrWhiteSpace(g.Text)))
        {
            if (!GatewayAddress.TryParse(entry.Text, out var gateway))
            {
                await _dialogs.AlertAsync("Invalid gateway", $"\"{entry.Text}\" is not a valid gateway. Use host, host:port or https://host:port.");
                return;
            }

            gateways.Add(gateway);
        }

        var profile = (_original ?? new VpnProfile { Name = "" }) with
        {
            Name = Name.Trim(),
            Description = NullIfBlank(Description),
            Gateways = gateways,
            AuthMode = SaveLogin ? AuthMode.SaveLogin : AuthMode.PromptOnLogin,
            Username = NullIfBlank(Username),
            ClientCertificatePath = NullIfBlank(ClientCertificatePath),
            ClientKeyPath = NullIfBlank(ClientKeyPath),
            ClientKeyInKeychain = KeyInKeychain && string.IsNullOrWhiteSpace(ClientKeyPath),
            TrustedCertDigests = [.. TrustedCerts.Select(c => c.Digest)],
            AutoConnect = AutoConnect,
            AlwaysUp = AlwaysUp,
        };

        var files = ProfileFiles.Check(profile);
        var problems = profile.Validate().Concat(files.Errors).ToList();
        if (problems.Count > 0)
        {
            await _dialogs.AlertAsync("Cannot save", string.Join("\n", problems));
            return;
        }

        // A key file is moved into the Keychain: the profile then keeps no path to it.
        if (profile.ClientKeyPath is { } keyPath)
        {
            try
            {
                await _session.StoreClientKeyAsync(profile.Id, await File.ReadAllTextAsync(keyPath));
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Cannot save", $"Could not store the client key in the Keychain: {ex.Message}");
                return;
            }

            profile = profile with { ClientKeyPath = null, ClientKeyInKeychain = true };
        }
        else if (_original?.ClientKeyInKeychain == true && !profile.ClientKeyInKeychain)
        {
            await _session.ForgetClientKeyAsync(profile.Id);
        }

        await _profiles.SaveAsync(profile);
        _session.OnProfileSaved(profile);
        if (_original?.AuthMode == AuthMode.SaveLogin && profile.AuthMode == AuthMode.PromptOnLogin)
        {
            await _session.ForgetPasswordAsync(profile.Id);
        }

        if (files.Warnings.Count > 0)
        {
            await _dialogs.AlertAsync("Saved, with a warning", string.Join("\n", files.Warnings));
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
