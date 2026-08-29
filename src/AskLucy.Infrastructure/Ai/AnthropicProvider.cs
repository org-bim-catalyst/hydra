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

internal static partial class AnthropicProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient Anthropic failure, retrying once after {DelayMs}ms")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, Exception exception, double delayMs);
}

/// <summary>
/// One of four <see cref="IAIProvider"/> implementations (specs/005-multi-provider-ai-engine).
/// Unlike <see cref="OpenAIProvider"/>, this has no legacy single-model call sites — it is
/// only ever resolved via <see cref="IAIProviderResolver"/> under the keyed <c>"anthropic"</c>
/// registration, so its credential comes from the admin-managed, Data-Protection-encrypted
/// <see cref="AIProvider.CredentialCiphertext"/> (data-model.md: "excluded from every query
/// projection except the one used to build the outbound HTTP call"), never from
/// <see cref="AnthropicOptions.ApiKey"/> (which stays an empty placeholder — constitution §8).
/// </summary>
public sealed class AnthropicProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AnthropicOptions> options,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector,
    ILogger<AnthropicProvider> logger) : IAIProvider
{
    private const string ProviderKey = "anthropic";

    /// <summary>specs/043 FR-028a - the vendor's documented maximum page size, so the catalog normally fits in one round trip.</summary>
    private const int ModelPageSize = 1000;

    /// <summary>Bounds the pagination loop so a malformed continuation cannot spin forever.</summary>
    private const int MaxModelPages = 20;
    private const int DefaultMaxTokens = 4096;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly AnthropicOptions _options = options.Value;

    public string ProviderName => "Anthropic";

    public string ChatModel => _options.ChatModel;

    public string ImageModel => throw new NotSupportedException("Anthropic does not support image generation.");

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
            var payload = BuildPayload(messages, model, parameters, stream: false);

            using var response = await client.PostAsJsonAsync("messages", payload, ct);
            await EnsureSuccessAsync(response, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var content = ExtractText(document.RootElement);
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
        var payload = BuildPayload(messages, model, parameters, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
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

        int? inputTokens = null;
        int? outputTokens = null;

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
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

                switch (eventType)
                {
                    case "message_start":
                        if (root.TryGetProperty("message", out var message) && message.TryGetProperty("usage", out var startUsage)
                            && startUsage.TryGetProperty("input_tokens", out var inputTokensElement))
                        {
                            inputTokens = inputTokensElement.GetInt32();
                        }
                        break;

                    case "content_block_delta":
                        if (root.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var textElement))
                        {
                            var chunk = textElement.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                yield return new StreamChunk(chunk);
                            }
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("usage", out var deltaUsage) && deltaUsage.TryGetProperty("output_tokens", out var outputTokensElement))
                        {
                            outputTokens = outputTokensElement.GetInt32();
                        }
                        break;

                    case "message_stop":
                        yield return new StreamChunk(ContentDelta: null, Usage: new ChatUsage(inputTokens, outputTokens, null, null, (int)stopwatch.ElapsedMilliseconds));
                        yield break;
                }
            }
        }
    }

    public Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Anthropic does not support image generation.");

    public Task<Uri> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Anthropic does not support image generation.");

    public Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Anthropic does not support audio transcription.");

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        AiProviderResponseClassifier.ProbeAsync(
            async ct =>
            {
                using var client = await CreateClientAsync(ct);
                using var response = await client.GetAsync("models", ct);
                await EnsureSuccessAsync(response, ct);
            },
            ProviderName,
            logger,
            cancellationToken);

    public Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        AiProviderResponseClassifier.TranslateAsync<IReadOnlyList<ProviderModelInfo>>(
            async ct =>
            {
                using var client = await CreateClientAsync(ct);

                var results = new List<ProviderModelInfo>();
                string? afterId = null;
                var pagesRead = 0;

                // specs/043 FR-028a: Anthropic paginates this endpoint with a small default
                // page size, so reading only the first page silently omits models while the
                // diff still looks successful.
                while (true)
                {
                    var url = $"models?limit={ModelPageSize}";
                    if (!string.IsNullOrEmpty(afterId))
                    {
                        url += $"&after_id={Uri.EscapeDataString(afterId)}";
                    }

                    using var response = await client.GetAsync(url, ct);
                    await EnsureSuccessAsync(response, ct);

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                    string? lastId = null;
                    foreach (var model in document.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var id = model.GetProperty("id").GetString();
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        lastId = id;
                        var displayName = model.TryGetProperty("display_name", out var displayNameElement) ? displayNameElement.GetString() ?? id : id;

                        // Anthropic's models list carries no context-window/capability metadata
                        // - a rough starting point for an administrator to review via the
                        // sync-diff flow (research.md Decision 5). specs/043 FR-029: absent is
                        // null, never a fabricated 0.
                        results.Add(new ProviderModelInfo(
                            id, displayName, ContextWindowTokens: null, MaxOutputTokens: null,
                            new AIModelCapabilities(
                                Streaming: true, Vision: true, FunctionCalling: true, JsonMode: false,
                                Reasoning: false, Embeddings: false, ImageInput: true, ImageOutput: false, Audio: false)));
                    }

                    var hasMore = document.RootElement.TryGetProperty("has_more", out var hasMoreElement)
                        && hasMoreElement.ValueKind == JsonValueKind.True;

                    if (!hasMore || string.IsNullOrEmpty(lastId))
                    {
                        break;
                    }

                    if (++pagesRead >= MaxModelPages)
                    {
                        throw AiProviderResponseClassifier.Create(AiProviderFailureKind.ResponseNotUnderstood, ProviderName);
                    }

                    afterId = lastId;
                }

                return results;
            },
            ProviderName,
            cancellationToken);

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetByKeyAsync(ProviderKey, cancellationToken)
            ?? throw new AiProviderNotConfiguredException("Anthropic is not configured in the provider catalog.");

        if (provider.CredentialCiphertext is null)
        {
            throw new AiProviderNotConfiguredException("Anthropic has no credential configured. An administrator must set one.");
        }

        // specs/043 FR-004 - see GoogleGeminiProvider.CreateClientAsync for why this matters.
        string apiKey;
        try
        {
            apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw AiProviderResponseClassifier.Create(AiProviderFailureKind.CredentialUnreadable, ProviderName, retryAfter: null, ex);
        }

        var client = httpClientFactory.CreateClient("Anthropic");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", _options.ApiVersion);
        return client;
    }

    private static Dictionary<string, object?> BuildPayload(IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, bool stream)
    {
        // Anthropic takes the system prompt as a top-level field, not a message with role
        // "system" — extract any ChatRole.System entries the Application layer assembled
        // (same convention OpenAIProvider.BuildChatPayload documents) and join them.
        var systemPrompt = string.Join(
            "\n\n", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        var conversationMessages = messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new { role = m.Role == ChatRole.Assistant ? "assistant" : "user", content = m.Content })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = conversationMessages,
            // Anthropic requires max_tokens on every request, unlike OpenAI where it's
            // optional — fall back to a conservative default when the caller didn't supply one.
            ["max_tokens"] = parameters?.MaxTokens ?? DefaultMaxTokens,
            ["stream"] = stream,
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            payload["system"] = systemPrompt;
        }

        if (parameters is not null)
        {
            if (parameters.Temperature is { } temperature) payload["temperature"] = temperature;
            if (parameters.TopP is { } topP) payload["top_p"] = topP;
            if (parameters.TopK is { } topK) payload["top_k"] = topK;
            if (parameters.StopSequences is { Count: > 0 } stop) payload["stop_sequences"] = stop;
        }

        return payload;
    }

    private static string ExtractText(JsonElement root)
    {
        var builder = new StringBuilder();
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "text"
                && block.TryGetProperty("text", out var textElement))
            {
                builder.Append(textElement.GetString());
            }
        }
        return builder.ToString();
    }

    private static ChatUsage ParseUsage(JsonElement root, int elapsedMs)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new ChatUsage(null, null, null, null, elapsedMs);
        }

        int? inputTokens = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : null;
        int? outputTokens = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : null;
        int? cachedTokens = usage.TryGetProperty("cache_read_input_tokens", out var c) ? c.GetInt32() : null;

        return new ChatUsage(inputTokens, outputTokens, cachedTokens, null, elapsedMs);
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

    /// <summary>specs/043: classification is delegated to the one shared table (research.md Decision 1) rather than re-derived from the status code here.</summary>
    private Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        AiProviderResponseClassifier.EnsureSuccessAsync(response, AiVendor.Anthropic, ProviderName, logger, cancellationToken);

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
            AnthropicProviderLog.RetryingAfterTransientFailure(logger, ex, RetryDelay.TotalMilliseconds);
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
