namespace PoultryFarm.Application.Common.Interfaces;

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}

public sealed record EmailSendResult(bool Success, string? Error = null, string? ProviderMessageId = null);
