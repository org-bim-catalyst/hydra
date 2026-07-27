using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

internal static partial class OpenAIProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient OpenAI failure, retrying once after {DelayMs}ms")]
    public static partial void RetryingAfterTransientFailure(ILogger logger, Exception exception, double delayMs);
}

/// <summary>
/// The sole <see cref="IAIProvider"/> implementation for this migration (FR-022). Every
/// call retries exactly once with backoff on a transient failure (timeout/5xx/429) before
/// raising <see cref="AiProviderUnavailableException"/>, per research.md Topic 4 and FR-032.
/// Replaces the legacy <c>ChatGPTController</c>'s inline, unauthenticated, unabstracted
/// OpenAI calls.
/// </summary>
public sealed class OpenAIProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAIProvider> logger) : IAIProvider
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly OpenAIOptions _options = options.Value;

    public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        WithRetryAsync(async ct =>
        {
            using var client = CreateClient();
            var payload = new
            {
                model = _options.ChatModel,
                messages = messages.Select(ToOpenAiMessage),
                stream = false,
            };

            using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
            await EnsureSuccessAsync(response, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }, cancellationToken);

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var payload = new
        {
            model = _options.ChatModel,
            messages = messages.Select(ToOpenAiMessage),
            stream = true,
        };

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
                var delta = document.RootElement.GetProperty("choices")[0].GetProperty("delta");

                if (delta.TryGetProperty("content", out var contentElement))
                {
                    var chunk = contentElement.GetString();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }
    }

    public Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default) =>
        WithRetryAsync(async ct =>
        {
            using var client = CreateClient();
            var payload = new { model = _options.ImageModel, prompt, n = 1, size = "1024x1024" };

            using var response = await client.PostAsJsonAsync("images/generations", payload, ct);
            await EnsureSuccessAsync(response, ct);

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
            await EnsureSuccessAsync(response, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return document.RootElement.GetProperty("text").GetString() ?? string.Empty;
        }, cancellationToken);

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
        catch (Exception ex) when (IsTransient(ex))
        {
            OpenAIProviderLog.RetryingAfterTransientFailure(logger, ex, RetryDelay.TotalMilliseconds);
            await Task.Delay(RetryDelay, cancellationToken);

            try
            {
                return await operation(cancellationToken);
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
