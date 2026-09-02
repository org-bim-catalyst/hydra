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

internal static partial class OpenRouterProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient OpenRouter failure, retrying once after {DelayMs}ms")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, Exception exception, double delayMs);
}

/// <summary>
/// One of four <see cref="IAIProvider"/> implementations (specs/005-multi-provider-ai-engine).
/// OpenAI-compatible wire format (research.md Decision 1), routed by a `"vendor/model"`-style
/// model key. Same credential-sourcing rule as <see cref="AnthropicProvider"/>/
/// <see cref="GoogleGeminiProvider"/> — reads the admin-managed, encrypted
/// <see cref="AIProvider.CredentialCiphertext"/>, not <see cref="OpenRouterOptions.ApiKey"/>.
/// </summary>
public sealed class OpenRouterProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> options,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector,
    ILogger<OpenRouterProvider> logger) : IAIProvider
{
    private const string ProviderKey = "openrouter";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly OpenRouterOptions _options = options.Value;

    public string ProviderName => "OpenRouter";

    public string ChatModel => _options.ChatModel;

    public string ImageModel => throw new NotSupportedException("OpenRouter image generation is not supported by this provider.");

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
            using var client = await CreateClientAsync(ct);
            var payload = BuildChatPayload(messages, model, parameters, stream: false);

            using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
            await EnsureSuccessAsync(response, ct);

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
        using var client = await CreateClientAsync(cancellationToken);
        var payload = BuildChatPayload(messages, model, parameters, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload),
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
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

                if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
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
        throw new NotSupportedException("OpenRouter image generation is not supported by this provider.");

    public Task<Uri> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("OpenRouter image generation is not supported by this provider.");

    public Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("OpenRouter audio transcription is not supported by this provider.");

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await CreateClientAsync(cancellationToken);
            using var response = await client.GetAsync("models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsTransient(ex) || ex is AiProviderAuthenticationException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await client.GetAsync("models", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<ProviderModelInfo>();
        foreach (var model in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = model.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var displayName = model.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? id : id;
            var contextLength = model.TryGetProperty("context_length", out var contextElement) ? contextElement.GetInt32() : 0;

            // OpenRouter's catalog carries no output-token-limit/capability metadata — a rough
            // starting point for an administrator to review via the sync-diff flow (research.md
            // Decision 5), matching OpenAIProvider.ListAvailableModelsAsync's same caveat.
            results.Add(new ProviderModelInfo(
                id, displayName, contextLength, MaxOutputTokens: 0,
                new AIModelCapabilities(
                    Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false,
                    Reasoning: false, Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false)));
        }

        return results;
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetByKeyAsync(ProviderKey, cancellationToken)
            ?? throw new AiProviderAuthenticationException("OpenRouter is not configured in the provider catalog.");

        if (provider.CredentialCiphertext is null)
        {
            throw new AiProviderAuthenticationException("OpenRouter has no credential configured. An administrator must set one.");
        }

        var apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);

        var client = httpClientFactory.CreateClient("OpenRouter");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static Dictionary<string, object?> BuildChatPayload(IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, bool stream)
    {
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
            if (parameters.JsonMode == true) payload["response_format"] = new { type = "json_object" };
        }

        return payload;
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

    private static ChatUsage ParseUsage(JsonElement root, int elapsedMs)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new ChatUsage(null, null, null, null, elapsedMs);
        }

        int? inputTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : null;
        int? outputTokens = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : null;

        return new ChatUsage(inputTokens, outputTokens, null, null, elapsedMs);
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"OpenRouter rejected the configured credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("OpenRouter rate-limited this request.", retryAfter);
        }

        throw new HttpRequestException(
            $"OpenRouter request failed with {(int)response.StatusCode}: {body}",
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
            throw;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            OpenRouterProviderLog.RetryingAfterTransientFailure(logger, ex, RetryDelay.TotalMilliseconds);
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
