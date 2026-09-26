using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PoultryFarm.Api.Services;

public sealed class AiProviderClient(HttpClient httpClient, IConfiguration configuration, ILogger<AiProviderClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateAsync(string input, CancellationToken cancellationToken = default)
    {
        var provider = configuration["AI:Provider"] ?? "OpenAI";
        return provider.Trim().ToLowerInvariant() switch
        {
            "gemini" or "google" => await GenerateWithGeminiAsync(input, cancellationToken),
            "openai" => await GenerateWithOpenAiAsync(input, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported AI provider '{provider}'. Use OpenAI or Gemini.")
        };
    }

    private async Task<string> GenerateWithOpenAiAsync(string input, CancellationToken cancellationToken)
    {
        var apiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_api_key_here")
        {
            throw new InvalidOperationException("OpenAI API key is missing. Set OpenAI:ApiKey, OPENAI_API_KEY, or switch AI:Provider to Gemini.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new
        {
            model = configuration["OpenAI:Model"] ?? "gpt-4.1-mini",
            input
        };

        request.Content = CreateJsonContent(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var result = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException("OpenAI", result);
        }

        return ExtractOpenAiText(result) ?? string.Empty;
    }

    private async Task<string> GenerateWithGeminiAsync(string input, CancellationToken cancellationToken)
    {
        var apiKey = ResolveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your_gemini_api_key_here")
        {
            throw new InvalidOperationException("Gemini API key is missing. Set Gemini:ApiKey or GEMINI_API_KEY.");
        }

        var configuredModel = configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite";
        // Prefer lite first under current Google capacity pressure; heavier flash models often return 503.
        var modelsToTry = new List<string>
        {
            configuredModel,
            "gemini-3.1-flash-lite",
            "gemini-3.8-flash",
            "gemini-flash-latest"
        }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? lastFailure = null;
        Exception? lastException = null;
        foreach (var model in modelsToTry)
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = CreateJsonContent(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = input }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 1.0,
                    maxOutputTokens = 768,
                    // Keep Flash thinking light so farm replies stay quick.
                    thinkingConfig = new { thinkingLevel = "low" }
                }
            });

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                var result = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return ExtractGeminiText(result) ?? string.Empty;
                }

                lastFailure = result;
                logger.LogWarning("Gemini request failed for model {Model} with {StatusCode}: {Body}", model, response.StatusCode, result);

                // Auth failures are fatal; everything else (503, 404 retired model, 400 config) tries the next model.
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    break;
                }

                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                logger.LogWarning(ex, "Gemini request timed out for model {Model}.", model);
                throw;
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new AiProviderException("Gemini", lastFailure ?? "Gemini request failed.");
    }

    private StringContent CreateJsonContent(object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private string? ResolveOpenAiApiKey() =>
        configuration["OpenAI:ApiKey"]
        ?? configuration["OPENAI_API_KEY"]
        ?? configuration["OPEN_API_KEY"]
        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? Environment.GetEnvironmentVariable("OPEN_API_KEY");

    private string? ResolveGeminiApiKey() =>
        configuration["Gemini:ApiKey"]
        ?? configuration["GEMINI_API_KEY"]
        ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    private static string? ExtractOpenAiText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        return FindFirstText(document.RootElement);
    }

    private static string? ExtractGeminiText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() == 0)
        {
            return null;
        }

        // Thinking models may return non-text parts first; collect visible text parts.
        var texts = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("thought", out var thought) &&
                thought.ValueKind is JsonValueKind.True)
            {
                continue;
            }

            if (part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(text.GetString()))
            {
                texts.Add(text.GetString()!);
            }
        }

        return texts.Count == 0 ? null : string.Join("\n", texts);
    }

    private static string? FindFirstText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var value = FindFirstText(property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindFirstText(item);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool IsRetryableStatusCode(int statusCode) =>
        statusCode is
            StatusCodes.Status408RequestTimeout or
            StatusCodes.Status429TooManyRequests or
            StatusCodes.Status500InternalServerError or
            StatusCodes.Status502BadGateway or
            StatusCodes.Status503ServiceUnavailable or
            StatusCodes.Status504GatewayTimeout;
}

public sealed class AiProviderException(string provider, string responseBody)
    : InvalidOperationException($"{provider} request failed: {responseBody}")
{
    public string Provider { get; } = provider;
    public string ResponseBody { get; } = responseBody;
}
