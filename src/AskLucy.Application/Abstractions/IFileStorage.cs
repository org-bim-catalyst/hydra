namespace AskLucy.Application.Abstractions;

/// <summary>
/// File storage abstraction (docs/ARCHITECTURE.md &#167;17). This migration's only
/// implementation is <c>LocalFileStorage</c> (server filesystem); cloud storage
/// implementations are a future swap behind this same interface.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileNameHint, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues and validates short-lived signed download URLs (research.md Topic 6),
/// avoiding ever exposing a physical file path to the client.
/// </summary>
public interface ISignedUrlService
{
    (string Expires, string Signature) Sign(string resourceId, TimeSpan lifetime);

    bool IsValid(string resourceId, string expires, string signature);
}
