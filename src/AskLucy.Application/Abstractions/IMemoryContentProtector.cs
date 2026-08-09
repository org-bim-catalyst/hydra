namespace AskLucy.Application.Abstractions;

/// <summary>
/// Encrypts/decrypts memory content at rest (research.md Decision 12). A dedicated interface
/// rather than reusing <see cref="IAiCredentialProtector"/> directly — same underlying Data
/// Protection mechanism, but a distinct purpose string (proper purpose isolation between two
/// conceptually unrelated data categories: provider credentials vs. personal memory content),
/// discovered as the more correct design during <c>/speckit-implement</c> after initially
/// considering a literal reuse of the credential protector's singleton.
/// </summary>
public interface IMemoryContentProtector
{
    string Protect(string plaintext);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> if <paramref name="ciphertext"/> is invalid or was encrypted under a different key ring.</summary>
    string Unprotect(string ciphertext);
}
