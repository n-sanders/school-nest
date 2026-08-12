using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SchoolTracking.Services;

public class OpenRouterImageException(string message, bool isModeration = false) : Exception(message)
{
    public bool IsModeration { get; } = isModeration;
}

public class OpenRouterImageService(HttpClient http)
{
    private static readonly HashSet<string> ModerationCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "content_policy_violation",
        "image_content_policy_violation",
        "refusal"
    };

    public async Task<(byte[] Bytes, string ContentType)> GenerateAsync(
        string apiKey, string model, string prompt, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/images");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent.Create(new
        {
            model,
            prompt,
            aspect_ratio = "16:9",
            resolution = "1K"
        });

        using var res = await http.SendAsync(req, cancellationToken);
        var body = await res.Content.ReadAsStringAsync(cancellationToken);

        if (!res.IsSuccessStatusCode)
        {
            var parsed = ClassifyError(body, res.StatusCode);
            throw new OpenRouterImageException(
                parsed.Message ?? $"Image generation failed ({(int)res.StatusCode})",
                parsed.IsModeration);
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            throw new OpenRouterImageException("Image generation returned no images");

        var first = data[0];
        if (!first.TryGetProperty("b64_json", out var b64El) || string.IsNullOrEmpty(b64El.GetString()))
            throw new OpenRouterImageException("Image generation did not return image data");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(b64El.GetString()!);
        }
        catch (FormatException)
        {
            throw new OpenRouterImageException("Image generation returned invalid image data");
        }

        var contentType = "image/png";
        if (first.TryGetProperty("media_type", out var mt) && mt.GetString() is { Length: > 0 } media)
            contentType = media;

        return (bytes, contentType);
    }

    private static (string? Message, bool IsModeration) ClassifyError(string body, HttpStatusCode status)
    {
        string? message = null;
        var isModeration = false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error_type", out var errorType) && errorType.ValueKind == JsonValueKind.String)
            {
                var type = errorType.GetString();
                if (type is not null && ModerationCodes.Contains(type))
                    isModeration = true;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    message = error.GetString();
                }
                else if (error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var msgEl))
                        message = msgEl.GetString();

                    if (error.TryGetProperty("code", out var codeEl))
                    {
                        if (codeEl.ValueKind == JsonValueKind.String)
                        {
                            var code = codeEl.GetString();
                            if (code is not null && ModerationCodes.Contains(code))
                                isModeration = true;
                        }
                    }

                    if (error.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                    {
                        if (metadata.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array
                            && reasons.GetArrayLength() > 0)
                        {
                            isModeration = true;
                            var parts = new List<string>();
                            foreach (var reason in reasons.EnumerateArray())
                            {
                                if (reason.ValueKind == JsonValueKind.String && reason.GetString() is { Length: > 0 } r)
                                    parts.Add(r);
                            }
                            if (parts.Count > 0)
                                message = string.Join("; ", parts);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            /* ignore unparseable error bodies */
        }

        if (LooksLikeModerationMessage(message))
            isModeration = true;

        if (status == HttpStatusCode.Forbidden && LooksLikeModerationMessage(message ?? body))
            isModeration = true;

        if (message is { Length: > 500 })
            message = message[..500];

        return (message, isModeration);
    }

    private static bool LooksLikeModerationMessage(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("moderation")
            || t.Contains("content policy")
            || t.Contains("flagged")
            || t.Contains("prohibited");
    }
}
