using System.Diagnostics;
using System.Net;
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

internal static partial class GoogleGeminiProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient Google Gemini failure, retrying once after {DelayMs}ms")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, Exception exception, double delayMs);
}

/// <summary>
/// One of four <see cref="IAIProvider"/> implementations (specs/005-multi-provider-ai-engine).
/// Same credential-sourcing rule as <see cref="AnthropicProvider"/> — reads the admin-managed,
/// encrypted <see cref="AIProvider.CredentialCiphertext"/>, not <see cref="GoogleGeminiOptions.ApiKey"/>.
/// Gemini passes its API key as a query-string parameter, not a header (research.md Decision 1).
/// </summary>
public sealed class GoogleGeminiProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleGeminiOptions> options,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector,
    ILogger<GoogleGeminiProvider> logger) : IAIProvider
{
    private const string ProviderKey = "google-gemini";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly GoogleGeminiOptions _options = options.Value;

    public string ProviderName => "Google Gemini";

    public string ChatModel => _options.ChatModel;

    public string ImageModel => throw new NotSupportedException("Google Gemini image generation is not supported by this provider.");

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
            var (client, apiKey) = await CreateClientAsync(ct);
            using var httpClient = client;
            var payload = BuildPayload(messages, parameters);

            using var response = await httpClient.PostAsJsonAsync($"models/{model}:generateContent?key={apiKey}", payload, ct);
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
        var (client, apiKey) = await CreateClientAsync(cancellationToken);
        using var httpClient = client;
        var payload = BuildPayload(messages, parameters);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:streamGenerateContent?alt=sse&key={apiKey}")
        {
            Content = JsonContent.Create(payload),
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;

                var text = ExtractText(root);
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new StreamChunk(text);
                }

                if (root.TryGetProperty("usageMetadata", out _))
                {
                    yield return new StreamChunk(ContentDelta: null, Usage: ParseUsage(root, (int)stopwatch.ElapsedMilliseconds));
                }
            }
        }
    }

    public Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Google Gemini image generation is not supported by this provider.");

    public Task<Uri> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Google Gemini image generation is not supported by this provider.");

    public Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Google Gemini audio transcription is not supported by this provider.");

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (client, apiKey) = await CreateClientAsync(cancellationToken);
            using var httpClient = client;
            using var response = await httpClient.GetAsync($"models?key={apiKey}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsTransient(ex) || ex is AiProviderAuthenticationException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var (client, apiKey) = await CreateClientAsync(cancellationToken);
        using var httpClient = client;
        using var response = await httpClient.GetAsync($"models?key={apiKey}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<ProviderModelInfo>();
        foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            var name = model.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var modelKey = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            var displayName = model.TryGetProperty("displayName", out var displayNameElement) ? displayNameElement.GetString() ?? modelKey : modelKey;
            // Gemini's catalog spans far more than chat models (embeddings, TTS, image/video
            // generation, live-audio) — several of those report inputTokenLimit/outputTokenLimit
            // as a JSON null rather than omitting the field, and GetInt32() throws on a null-kind
            // element. Guarding the ValueKind here (not just TryGetProperty's presence check)
            // stops one such model from taking down the entire catalog listing.
            var contextWindow = model.TryGetProperty("inputTokenLimit", out var inputLimit) && inputLimit.ValueKind == JsonValueKind.Number
                ? inputLimit.GetInt32() : 0;
            var maxOutput = model.TryGetProperty("outputTokenLimit", out var outputLimit) && outputLimit.ValueKind == JsonValueKind.Number
                ? outputLimit.GetInt32() : 0;

            results.Add(new ProviderModelInfo(
                modelKey, displayName, contextWindow, maxOutput,
                new AIModelCapabilities(
                    Streaming: true, Vision: true, FunctionCalling: true, JsonMode: true,
                    Reasoning: false, Embeddings: false, ImageInput: true, ImageOutput: false, Audio: false)));
        }

        return results;
    }

    private async Task<(HttpClient Client, string ApiKey)> CreateClientAsync(CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetByKeyAsync(ProviderKey, cancellationToken)
            ?? throw new AiProviderAuthenticationException("Google Gemini is not configured in the provider catalog.");

        if (provider.CredentialCiphertext is null)
        {
            throw new AiProviderAuthenticationException("Google Gemini has no credential configured. An administrator must set one.");
        }

        var apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);

        var client = httpClientFactory.CreateClient("GoogleGemini");
        client.BaseAddress = new Uri(_options.BaseUrl);
        return (client, apiKey);
    }

    private static Dictionary<string, object?> BuildPayload(IReadOnlyList<ChatMessage> messages, GenerationParametersDto? parameters)
    {
        var systemInstructionText = string.Join(
            "\n\n", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        var contents = messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new
            {
                role = m.Role == ChatRole.Assistant ? "model" : "user",
                parts = new[] { new { text = m.Content } },
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = contents,
        };

        if (!string.IsNullOrEmpty(systemInstructionText))
        {
            payload["systemInstruction"] = new { parts = new[] { new { text = systemInstructionText } } };
        }

        if (parameters is not null)
        {
            var generationConfig = new Dictionary<string, object?>();
            if (parameters.Temperature is { } temperature) generationConfig["temperature"] = temperature;
            if (parameters.TopP is { } topP) generationConfig["topP"] = topP;
            if (parameters.TopK is { } topK) generationConfig["topK"] = topK;
            if (parameters.MaxTokens is { } maxTokens) generationConfig["maxOutputTokens"] = maxTokens;
            if (parameters.StopSequences is { Count: > 0 } stop) generationConfig["stopSequences"] = stop;
            if (parameters.JsonMode == true) generationConfig["responseMimeType"] = "application/json";

            if (generationConfig.Count > 0)
            {
                payload["generationConfig"] = generationConfig;
            }
        }

        return payload;
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        if (candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    builder.Append(textElement.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static ChatUsage ParseUsage(JsonElement root, int elapsedMs)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new ChatUsage(null, null, null, null, elapsedMs);
        }

        int? inputTokens = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : null;
        int? outputTokens = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : null;
        int? cachedTokens = usage.TryGetProperty("cachedContentTokenCount", out var cc) ? cc.GetInt32() : null;

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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"Google Gemini rejected the configured credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("Google Gemini rate-limited this request.", retryAfter);
        }

        throw new HttpRequestException(
            $"Google Gemini request failed with {(int)response.StatusCode}: {body}",
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
            GoogleGeminiProviderLog.RetryingAfterTransientFailure(logger, ex, RetryDelay.TotalMilliseconds);
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
