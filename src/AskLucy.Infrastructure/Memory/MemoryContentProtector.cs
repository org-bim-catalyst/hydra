using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AskLucy.Infrastructure.Memory;

/// <summary>Mirrors <c>AiCredentialProtector</c>'s use of Data Protection, applied to memory content under its own purpose string (research.md Decision 12).</summary>
public sealed class MemoryContentProtector : IMemoryContentProtector
{
    private readonly IDataProtector _protector;

    public MemoryContentProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("AskLucy.MemoryContent");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
