using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.TheDigitalCore;

public interface ITheDigitalCoreAuthService
{
    /// <summary>Cached until shortly before expiry; a single in-flight exchange is shared across
    /// concurrent callers rather than each racing its own token request (mirrors the caching
    /// pattern TheDigitalCore's own <c>ForgeAuthService</c> uses for its upstream, Autodesk Forge).</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class TheDigitalCoreAuthService(
    IHttpClientFactory httpClientFactory, IOptions<TheDigitalCoreIntegrationOptions> options) : ITheDigitalCoreAuthService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is { } token && DateTimeOffset.UtcNow < _expiresAt)
        {
            return token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is { } cached && DateTimeOffset.UtcNow < _expiresAt)
            {
                return cached;
            }

            var client = httpClientFactory.CreateClient("TheDigitalCore");
            using var response = await client.PostAsJsonAsync(
                "api/service-auth/token",
                new { clientId = options.Value.ServiceAccountClientId, clientSecret = options.Value.ServiceAccountClientSecret },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new TheDigitalCoreIntegrationException(
                    $"TheDigitalCore rejected the service-account token exchange ({(int)response.StatusCode}).");
            }

            var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new TheDigitalCoreIntegrationException("TheDigitalCore returned an empty token response.");

            _cachedToken = result.AccessToken;
            _expiresAt = result.ExpiresAtUtc.AddSeconds(-30); // refresh a little early, never race up against real expiry
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
