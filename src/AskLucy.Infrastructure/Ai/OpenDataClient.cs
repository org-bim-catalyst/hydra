using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// Base class for open data source API clients.
/// Provides common functionality for HTTP requests, retry policy, rate limiting,
/// and JSON deserialization of varying source response formats.
/// </summary>
public class OpenDataClient : IOpenDataClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenDataClient> _logger;
    private readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(3, 3); // Max 3 concurrent requests
    private readonly RequestCounter _requestCounter = new RequestCounter();
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenDataClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public OpenDataClient(HttpClient httpClient, ILogger<OpenDataClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    /// <summary>
    /// Gets the HTTP client instance.
    /// </summary>
    public HttpClient GetHttpClient() => _httpClient;

    /// <summary>
    /// Sends a GET request to the specified URI with retry policy and rate limiting.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response into.</typeparam>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="retries">Number of retry attempts on failure.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized response.</returns>
    public async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken = default, int retries = 3)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var attempt = 0;
            HttpResponseMessage response = null;

            while (attempt < retries)
            {
                attempt++;
                try
                {
                    response = await _httpClient.GetAsync(uri, cancellationToken);

                    // Success status codes
                    if (response.IsSuccessStatusCode)
                        return response;

                    // Try to parse error detail for logging
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "OpenDataClient GET attempt {Attempt} failed for {Uri}: Status {Status}, Body: {Body}",
                        attempt, uri, (int)response.StatusCode, errorBody);
                }
                catch (TaskCanceledException) when (attempt < retries)
                {
                    _logger.LogWarning(
                        "OpenDataClient GET attempt {Attempt} timed out for {Uri}, retrying...", attempt, uri);
                }
                catch (HttpRequestException) when (attempt < retries)
                {
                    _logger.LogWarning(
                        "OpenDataClient GET attempt {Attempt} had HTTP request error for {Uri}, retrying...", attempt, uri);
                }

                if (attempt < retries)
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }

            return response ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            _rateLimiter.Release();
            _requestCounter.Increment();
        }
    }

    /// <summary>
    /// Sends a POST request to the specified URI with retry policy and rate limiting.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response into.</typeparam>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="content">The request content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="retries">Number of retry attempts on failure.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized response.</returns>
    public async Task<HttpResponseMessage> PostAsync(Uri uri, StringContent content, CancellationToken cancellationToken = default, int retries = 3)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var attempt = 0;
            HttpResponseMessage response = null;

            while (attempt < retries)
            {
                attempt++;
                try
                {
                    response = await _httpClient.PostAsync(uri, content, cancellationToken);

                    if (response.IsSuccessStatusCode)
                        return response;

                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "OpenDataClient POST attempt {Attempt} failed for {Uri}: Status {Status}, Body: {Body}",
                        attempt, uri, (int)response.StatusCode, errorBody);
                }
                catch (TaskCanceledException) when (attempt < retries)
                {
                    _logger.LogWarning(
                        "OpenDataClient POST attempt {Attempt} timed out for {Uri}, retrying...", attempt, uri);
                }
                catch (HttpRequestException) when (attempt < retries)
                {
                    _logger.LogWarning(
                        "OpenDataClient POST attempt {Attempt} had HTTP request error for {Uri}, retrying...", attempt, uri);
                }

                if (attempt < retries)
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }

            return response ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            _rateLimiter.Release();
            _requestCounter.Increment();
        }
    }

    /// <summary>
    /// Deserializes the HTTP response content as the specified type.
    /// Handles multiple possible JSON formats gracefully.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized object, or null if deserialization fails.</returns>
    public async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response == null)
            return default;

        if (!response.IsSuccessStatusCode)
            return default;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
            return default;

        try
        {
            // Try direct deserialization first
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch
        {
            try
            {
                // Try deserializing as a generic object and extracting relevant properties
                var obj = JsonSerializer.Deserialize<JsonElement>(content);
                return ExtractValue<T>(obj);
            }
            catch
            {
                _logger.LogWarning(
                    "OpenDataClient deserialization failed for type {Type}. Response: {Content}",
                    typeof(T), content);
                return default;
            }
        }
    }

    /// <summary>
    /// Extracts a value from a JsonElement, handling various response formats.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="element">The JsonElement to extract from.</param>
    /// <returns>The extracted value, or null if extraction fails.</returns>
    private T ExtractValue<T>(JsonElement element)
    {
        // Try direct match
        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        catch
        {
            // If the element is an object, try to find common fields
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                // Try common field names across different data sources
                var properties = element.EnumerateObject();

                // Check for typical data source response fields
                foreach (var prop in properties)
                {
                    if (prop.NameEquals("name") || prop.NameEquals("title") || prop.NameEquals("label") || prop.NameEquals("nameAr"))
                    {
                        try
                        {
                            return JsonSerializer.Deserialize<T>(prop.Value.GetRawText());
                        }
                        catch { }
                    }
                }
            }
        }

        return default;
    }

    /// <summary>
    /// Counts requests made by this client for monitoring purposes.
    /// </summary>
    private class RequestCounter
    {
        private int _count;

        public void Increment() => Interlocked.Increment(ref _count);

        public int GetCount() => _count;
    }
}
