using CommunityToolkit.Mvvm.Input;

namespace OpenFortiClient.App.ViewModels;

/// <summary>One trusted gateway certificate (its SHA-256 digest) on the edit screen.</summary>
public sealed partial class TrustedCertEntry(string digest, Action<TrustedCertEntry> remove)
{
    public string Digest { get; } = digest;

    /// <summary>The first 16 hex digits; the whole digest is in the tooltip.</summary>
    public string Short => Digest.Length > 16 ? $"{Digest[..16]}…" : Digest;

    [RelayCommand]
    private void Remove() => remove(this);
}
