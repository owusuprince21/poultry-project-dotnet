using System.Text.Json;

namespace PoultryFarm.Blazor.Services;

public static class ApiErrorFormatter
{
    public static string Format(string? raw, string fallback = "Request failed.")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? fallback;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "detail", "message", "error" })
                {
                    if (root.TryGetProperty(key, out var property))
                    {
                        return ReadValue(property, fallback);
                    }
                }

                var first = root.EnumerateObject().FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first.Name))
                {
                    return ReadValue(first.Value, fallback);
                }
            }
        }
        catch (JsonException)
        {
            return raw;
        }

        return raw;
    }

    public static async Task<string> FormatHttpAsync(HttpResponseMessage response, string fallback = "Request failed.")
    {
        var raw = await response.Content.ReadAsStringAsync();
        return Format(raw, fallback);
    }

    public static string FormatException(Exception exception, string fallback = "Request failed.", Uri? apiBaseAddress = null)
    {
        if (exception is HttpRequestException httpException)
        {
            // StatusCode is set when the server responded (4xx/5xx); null means a true transport failure.
            if (httpException.StatusCode is null)
            {
                return apiBaseAddress is null
                    ? "The API is not reachable. Start the API server and try again."
                    : $"The API is not reachable at {apiBaseAddress}. Start the API server and try again.";
            }

            return string.IsNullOrWhiteSpace(httpException.Message)
                ? $"{fallback} ({(int)httpException.StatusCode})"
                : httpException.Message;
        }

        return string.IsNullOrWhiteSpace(exception.Message) ? fallback : exception.Message;
    }

    private static string ReadValue(JsonElement element, string fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Array => string.Join(" ", element.EnumerateArray().Select(x => ReadValue(x, fallback))),
            JsonValueKind.Object => Format(element.GetRawText(), fallback),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => fallback
        };
    }
}
