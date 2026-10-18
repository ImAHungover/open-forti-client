namespace OpenFortiClient.App.Services;

public sealed class ShellDialogService : IDialogService
{
    private const string CancelText = "Cancel";

    public Task AlertAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlertAsync(title, message, "OK"));

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = CancelText) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlertAsync(title, message, accept, cancel));

    public Task<string?> PromptAsync(string title, string message, Keyboard? keyboard = null) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
            (string?)await Shell.Current.DisplayPromptAsync(title, message, keyboard: keyboard ?? Keyboard.Default));

    public Task<string?> ChooseAsync(string title, params string[] options) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var choice = await Shell.Current.DisplayActionSheetAsync(title, CancelText, null, options);
            return choice is null or CancelText ? null : choice;
        });
}
