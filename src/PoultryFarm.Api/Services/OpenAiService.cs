namespace PoultryFarm.Api.Services;

public sealed class OpenAiService(AiProviderClient aiProviderClient)
{
    public Task<string> AskAsync(string message, CancellationToken cancellationToken = default) =>
        aiProviderClient.GenerateAsync(message, cancellationToken);
}
