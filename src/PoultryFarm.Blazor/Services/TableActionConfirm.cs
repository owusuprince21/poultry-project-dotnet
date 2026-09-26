using MudBlazor;

namespace PoultryFarm.Blazor.Services;

public sealed class TableActionConfirm(IDialogService dialogService)
{
    public async Task<bool> ConfirmAsync(string actionLabel, string? subject = null)
    {
        var message = string.IsNullOrWhiteSpace(subject)
            ? $"Are you sure you want to {actionLabel}?"
            : $"Are you sure you want to {actionLabel} \"{subject}\"?";

        var result = await dialogService.ShowMessageBoxAsync(
            "Confirm action",
            message,
            yesText: "Confirm",
            cancelText: "Cancel");

        return result == true;
    }
}
