namespace AskLucy.Application.Abstractions;

/// <summary>
/// Encrypts/decrypts an <see cref="AskLucy.Domain.Ai.AIProvider.CredentialCiphertext"/> value
/// (research.md Decision 4) — the same Data Protection mechanism <c>ISignedUrlService</c>
/// already uses for signed URLs, applied here to provider API keys instead.
/// </summary>
public interface IAiCredentialProtector
{
    string Protect(string rawCredential);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> if <paramref name="ciphertext"/> is invalid or was encrypted under a different key ring.</summary>
    string Unprotect(string ciphertext);
}
