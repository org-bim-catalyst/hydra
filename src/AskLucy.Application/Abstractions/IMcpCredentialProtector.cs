namespace AskLucy.Application.Abstractions;

/// <summary>
/// Encrypts/decrypts MCP server credential material (spec.md FR-045-FR-047, research.md Decision 7)
/// — structurally identical to <see cref="IAiCredentialProtector"/>, using a distinct Data
/// Protection purpose string so a key-ring compromise of one credential class doesn't
/// automatically unlock the other. Application/Domain never see a plaintext credential.
/// </summary>
public interface IMcpCredentialProtector
{
    string Protect(string rawCredential);

    string Unprotect(string ciphertext);
}
