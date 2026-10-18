namespace OpenFortiClient.App.Services;

/// <summary>Keeps view models free of page APIs, so they stay testable.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");

    /// <returns>The entered text, or null if cancelled.</returns>
    Task<string?> PromptAsync(string title, string message, Keyboard? keyboard = null);

    /// <returns>The chosen option, or null if cancelled.</returns>
    Task<string?> ChooseAsync(string title, params string[] options);
}
