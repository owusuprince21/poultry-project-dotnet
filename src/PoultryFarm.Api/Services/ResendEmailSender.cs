using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PoultryFarm.Application.Common.Interfaces;

namespace PoultryFarm.Api.Services;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Resend:ApiKey is not configured. Email to {Recipient} was not sent.", to);
            return new EmailSendResult(false, "Resend API key is not configured.");
        }

        var fromEmail = configuration["Resend:FromEmail"] ?? "onboarding@resend.dev";
        var fromName = configuration["Resend:FromName"] ?? "Akokɔ Papa";
        var from = $"{fromName} <{fromEmail}>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new ResendSendRequest(
            from,
            [to.Trim()],
            subject,
            htmlBody,
            textBody));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Resend email failed ({StatusCode}) for {Recipient}: {Body}",
                    (int)response.StatusCode,
                    to,
                    payload);
                return new EmailSendResult(false, $"Resend returned {(int)response.StatusCode}.");
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<ResendSendResponse>(payload);
            logger.LogInformation("Resend email sent to {Recipient}. Id={MessageId}", to, parsed?.Id);
            return new EmailSendResult(true, ProviderMessageId: parsed?.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resend email request failed for {Recipient}.", to);
            return new EmailSendResult(false, ex.Message);
        }
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record ResendSendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
