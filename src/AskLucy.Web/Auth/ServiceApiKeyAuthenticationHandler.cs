using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AskLucy.Application.Abstractions;
using AskLucy.Application.MapCaptures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Auth;

internal static partial class ServiceApiKeyAuthenticationLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Service API key rejected for {Path}: {Reason}")]
    public static partial void Rejected(ILogger logger, PathString path, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Service API key accepted for {Caller} on {Path}")]
    public static partial void Accepted(ILogger logger, string caller, PathString path);
}

/// <summary>
/// specs/083-viewer-map-capture T024 — authenticates a trusted service by a static key.
/// </summary>
/// <remarks>
/// The key is compared with <see cref="CryptographicOperations.FixedTimeEquals"/> so a caller
/// cannot learn a valid key from response timing. The presented key is never logged; only the
/// configured caller name and the request path are.
/// </remarks>
public sealed class ServiceApiKeyAuthenticationHandler(
    IOptionsMonitor<ServiceApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<ServiceApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    public const string ServiceRole = "Service";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var presented) || presented.Count == 0)
        {
            ServiceApiKeyAuthenticationLog.Rejected(Logger, Request.Path, "no key presented");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var candidate = presented[0];
        if (string.IsNullOrWhiteSpace(candidate))
        {
            ServiceApiKeyAuthenticationLog.Rejected(Logger, Request.Path, "empty key");
            return Task.FromResult(AuthenticateResult.Fail("The service API key is empty."));
        }

        var keys = Context.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MapCaptureOptions>>()
            .Value.ApiKeys;

        if (keys.Count == 0)
        {
            ServiceApiKeyAuthenticationLog.Rejected(Logger, Request.Path, "no service keys are configured");
            return Task.FromResult(AuthenticateResult.Fail("No service API keys are configured."));
        }

        foreach (var entry in keys)
        {
            if (string.IsNullOrEmpty(entry.Key) || !FixedTimeEquals(entry.Key, candidate))
            {
                continue;
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, entry.Name),
                new Claim(ClaimTypes.Name, entry.Name),
                new Claim(ClaimTypes.Role, ServiceRole),
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            ServiceApiKeyAuthenticationLog.Accepted(Logger, entry.Name, Request.Path);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        ServiceApiKeyAuthenticationLog.Rejected(Logger, Request.Path, "unknown key");
        return Task.FromResult(AuthenticateResult.Fail("The service API key is not recognised."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";

        var traceId = Context.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var correlationId)
            ? correlationId?.ToString()
            : Context.TraceIdentifier;

        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Service authentication required",
            status = StatusCodes.Status401Unauthorized,
            detail = "A valid X-Api-Key header is required for this endpoint.",
            traceId,
        };

        return Response.WriteAsJsonAsync(problem, Context.RequestAborted);
    }

    private static bool FixedTimeEquals(string expected, string presented)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);

        // CryptographicOperations.FixedTimeEquals requires equal lengths; hash both sides so a
        // length mismatch does not short-circuit the comparison.
        var expectedHash = SHA256.HashData(expectedBytes);
        var presentedHash = SHA256.HashData(presentedBytes);

        return CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash)
            && expectedBytes.Length == presentedBytes.Length;
    }
}
