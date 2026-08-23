using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

internal static partial class OpenAIProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient OpenAI failure, retrying once after {DelayMs}ms")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, Exception exception, double delayMs);

    // The middleware only logs unhandled exceptions at >= 500 (ProblemDetailsMiddleware),
    // so a 4xx rejection needs its own server-side record here — the client-facing Problem
    // Details response deliberately omits the raw provider body (specs/032).
    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI rejected the request with {StatusCode}: {Body}")]
    public static partial void RequestRejectedByProvider(ILogger logger, int statusCode, string body);
}

/// <summary>
/// One of four <see cref="IAIProvider"/> implementations (specs/005-multi-provider-ai-engine).
/// Registered both as the plain, unkeyed <see cref="IAIProvider"/> (for legacy single-model
/// call sites — Translate, image generation, <c>AppendMessageCommandHandler</c> attribution)
/// and as the keyed <c>"openai"</c> service resolved via <see cref="IAIProviderResolver"/> for
/// multi-provider chat/comparison. Every call retries exactly once with backoff on a
/// transient failure (timeout/5xx/429) before raising <see cref="AiProviderUnavailableException"/>
/// (auth failures raise <see cref="AiProviderAuthenticationException"/> instead — never
/// retried, since retrying a bad credential just wastes the retry budget); research.md
/// Decision 9. Replaces the legacy <c>ChatGPTController</c>'s inline, unauthenticated,
/// unabstracted OpenAI calls.
/// </summary>
public sealed class OpenAIProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAIProvider> logger) : IAIProvider
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly OpenAIOptions _options = options.Value;

    public string ProviderName => "OpenAI";

    public string ChatModel => _options.ChatModel;

    public string ImageModel => _options.ImageModel;

    public async Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var result = await ChatAsync(messages, _options.ChatModel, parameters: null, cancellationToken);
        return result.Content;
    }

    public Task<ChatCompletionResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, CancellationToken cancellationToken = default) =>
        WithRetryAsync(async ct =>
        {
            var stopwatch = Stopwatch.StartNew();
            using var client = CreateClient();
            var payload = BuildChatPayload(messages, model, parameters, stream: false);

            using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
            await EnsureSuccessAsync(response, logger, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var usage = ParseUsage(document.RootElement, (int)stopwatch.ElapsedMilliseconds);
            return new ChatCompletionResult(content, usage);
        }, cancellationToken);

    public IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        StripToContentAsync(StreamChatAsync(messages, _options.ChatModel, parameters: null, cancellationToken), cancellationToken);

    public async IAsyncEnumerable<StreamChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        GenerationParametersDto? parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = CreateClient();
        var payload = BuildChatPayload(messages, model, parameters, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload),
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, logger, cancellationToken);
        }
        catch (AiProviderAuthenticationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            throw new AiProviderUnavailableException("The AI service could not process your request. Please try again.", ex);
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line["data:".Length..].Trim();
                if (data == "[DONE]")
                {
                    yield break;
                }

                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;

                // The final chunk of a stream_options.include_usage=true stream has an empty
                // `choices` array and a top-level `usage` object instead of a delta.
                if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind != JsonValueKind.Null)
                {
                    yield return new StreamChunk(ContentDelta: null, Usage: ParseUsage(root, (int)stopwatch.ElapsedMilliseconds));
                    continue;
                }

                var choices = root.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentElement))
                {
                    var chunk = contentElement.GetString();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        yield return new StreamChunk(chunk);
                    }
                }
            }
        }
    }

    public Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default) =>
        GenerateImageAsync(prompt, _options.ImageModel, cancellationToken);

    public Task<Uri> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default) =>
        WithRetryAsync(async ct =>
        {
            using var client = CreateClient();
            var payload = new { model, prompt, n = 1, size = "1024x1024" };

            using var response = await client.PostAsJsonAsync("images/generations", payload, ct);
            await EnsureSuccessAsync(response, logger, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var url = document.RootElement.GetProperty("data")[0].GetProperty("url").GetString()
                ?? throw new AiProviderUnavailableException("The AI service returned no image.");

            return new Uri(url);
        }, cancellationToken);

    public Task<string> TranscribeAudioAsync(
        Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        WithRetryAsync(async ct =>
        {
            using var client = CreateClient();
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(audioContent);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent(_options.TranscriptionModel), "model");

            using var response = await client.PostAsync("audio/transcriptions", form, ct);
            await EnsureSuccessAsync(response, logger, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);

            // specs/033-hold-to-talk-and-echo-fix: a 2xx response with a malformed, empty, or
            // unexpected-shape body is a provider-side problem, not a client-request problem —
            // classify it the same way EnsureSuccessAsync classifies a bad status code, instead
            // of letting JsonException/InvalidOperationException fall through unclassified to a
            // generic 500.
            try
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                return document.RootElement.GetProperty("text").GetString() ?? string.Empty;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                throw new AiProviderUnavailableException(
                    "The AI service could not process your request. Please try again.", ex);
            }
        }, cancellationToken);

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("models", cancellationToken);
        await EnsureSuccessAsync(response, logger, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<ProviderModelInfo>();
        foreach (var model in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = model.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id) || !id.Contains("gpt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // OpenAI's models list carries no context-window/capability metadata — this is
            // deliberately a rough starting point for an administrator to review and correct
            // via the sync-diff flow (research.md Decision 5), not an authoritative catalog.
            results.Add(new ProviderModelInfo(
                id, id, ContextWindowTokens: 0, MaxOutputTokens: 0,
                new AIModelCapabilities(
                    Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false,
                    Reasoning: id.Contains('o', StringComparison.OrdinalIgnoreCase),
                    Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false)));
        }

        return results;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
    }

    private static Dictionary<string, object?> BuildChatPayload(IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, bool stream)
    {
        // SystemPrompt/DeveloperPrompt (if set) are expected to already be present in
        // `messages` as a ChatRole.System entry — the caller (Application layer) owns
        // assembling the message list, consistent with how TranslateCommandHandler already
        // builds its own system message today.
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(ToOpenAiMessage).ToList(),
            ["stream"] = stream,
        };

        if (stream)
        {
            payload["stream_options"] = new { include_usage = true };
        }

        if (parameters is not null)
        {
            if (parameters.Temperature is { } temperature) payload["temperature"] = temperature;
            if (parameters.TopP is { } topP) payload["top_p"] = topP;
            if (parameters.PresencePenalty is { } presencePenalty) payload["presence_penalty"] = presencePenalty;
            if (parameters.FrequencyPenalty is { } frequencyPenalty) payload["frequency_penalty"] = frequencyPenalty;
            if (parameters.MaxTokens is { } maxTokens) payload["max_tokens"] = maxTokens;
            if (parameters.StopSequences is { Count: > 0 } stop) payload["stop"] = stop;
            if (parameters.Seed is { } seed) payload["seed"] = seed;
            if (parameters.ReasoningLevel is { Length: > 0 } reasoningEffort) payload["reasoning_effort"] = reasoningEffort;
            if (parameters.JsonMode == true) payload["response_format"] = new { type = "json_object" };
        }

        return payload;
    }

    private static ChatUsage ParseUsage(JsonElement root, int elapsedMs)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new ChatUsage(null, null, null, null, elapsedMs);
        }

        int? inputTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : null;
        int? outputTokens = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : null;

        int? cachedTokens = usage.TryGetProperty("prompt_tokens_details", out var promptDetails)
            && promptDetails.TryGetProperty("cached_tokens", out var cached)
            ? cached.GetInt32() : null;

        int? reasoningTokens = usage.TryGetProperty("completion_tokens_details", out var completionDetails)
            && completionDetails.TryGetProperty("reasoning_tokens", out var reasoning)
            ? reasoning.GetInt32() : null;

        return new ChatUsage(inputTokens, outputTokens, cachedTokens, reasoningTokens, elapsedMs);
    }

    private static async IAsyncEnumerable<string> StripToContentAsync(
        IAsyncEnumerable<StreamChunk> chunks, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.ContentDelta is { Length: > 0 } delta)
            {
                yield return delta;
            }
        }
    }

    private static object ToOpenAiMessage(ChatMessage message) => new
    {
        role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.Assistant => "assistant",
            _ => "user",
        },
        content = message.Content,
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, ILogger logger, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"OpenAI rejected the configured credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("OpenAI rate-limited this request.", retryAfter);
        }

        if ((int)response.StatusCode is >= 400 and < 500)
        {
            // Any other 4xx (most plausibly a 400 rejecting a specific transcription upload)
            // is a request-level problem, not an unavailable/authentication/rate-limit
            // condition — classify it distinctly so it surfaces as an actionable message
            // instead of falling through to a generic 500 (specs/032). The raw body is
            // logged here (not in the exception message reaching the client) since
            // ProblemDetailsMiddleware only logs unhandled exceptions at >= 500.
            OpenAIProviderLog.RequestRejectedByProvider(logger, (int)response.StatusCode, body);
            throw new AiProviderRequestInvalidException($"OpenAI rejected the request with {(int)response.StatusCode}: {body}");
        }

        throw new HttpRequestException(
            $"OpenAI request failed with {(int)response.StatusCode}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (AiProviderAuthenticationException)
        {
            // Never retry a bad credential — retrying wastes the retry budget on a failure
            // mode that will not resolve itself between attempts (research.md Decision 9).
            throw;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            OpenAIProviderLog.RetryingAfterTransientFailure(logger, ex, RetryDelay.TotalMilliseconds);
            await Task.Delay(RetryDelay, cancellationToken);

            try
            {
                return await operation(cancellationToken);
            }
            catch (AiProviderAuthenticationException)
            {
                throw;
            }
            catch (Exception retryEx) when (IsTransient(retryEx))
            {
                throw new AiProviderUnavailableException(
                    "The AI service could not process your request. Please try again.", retryEx);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        TaskCanceledException => true,
        HttpRequestException httpEx => httpEx.StatusCode is null
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError,
        _ => false,
    };
}
