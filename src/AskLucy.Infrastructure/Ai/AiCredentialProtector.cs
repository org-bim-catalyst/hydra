using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AskLucy.Infrastructure.Ai;

/// <summary>Mirrors <c>SignedUrlService</c>'s use of Data Protection (research.md Decision 4), applied to provider credentials instead of signed URLs.</summary>
public sealed class AiCredentialProtector : IAiCredentialProtector
{
    private readonly IDataProtector _protector;

    public AiCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("AskLucy.AiProviderCredentials");
    }

    public string Protect(string rawCredential) => _protector.Protect(rawCredential);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
