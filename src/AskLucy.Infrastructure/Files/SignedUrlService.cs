using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AskLucy.Infrastructure.Files;

/// <summary>
/// HMAC-signed, short-lived download URLs via ASP.NET Core Data Protection
/// (research.md Topic 6) — never exposes the physical file path (constitution &#167;8).
/// </summary>
public sealed class SignedUrlService : ISignedUrlService
{
    private readonly ITimeLimitedDataProtector _protector;

    public SignedUrlService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("AskLucy.SignedUrls").ToTimeLimitedDataProtector();
    }

    public (string Expires, string Signature) Sign(string resourceId, TimeSpan lifetime)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime);
        var signature = _protector.Protect(resourceId, expiresAtUtc);
        return (expiresAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), signature);
    }

    public bool IsValid(string resourceId, string expires, string signature)
    {
        try
        {
            var unprotected = _protector.Unprotect(signature);
            return unprotected == resourceId;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
