using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>Mirrors <c>AiCredentialProtector</c> exactly (research.md Decision 7), with a distinct Data Protection purpose string so a key-ring compromise of one credential class doesn't automatically unlock the other.</summary>
public sealed class McpCredentialProtector : IMcpCredentialProtector
{
    private readonly IDataProtector _protector;

    public McpCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("AskLucy.McpServerCredentials");
    }

    public string Protect(string rawCredential) => _protector.Protect(rawCredential);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
