namespace AskLucy.Application.Abstractions;

/// <summary>
/// Temporary staging storage for a chunked upload session in progress (FR-005), keyed by the
/// <c>DocumentUploadSession</c>'s id — distinct from <see cref="IFileStorage"/>'s permanent
/// store, so an abandoned session never leaves orphaned files alongside real stored documents.
/// Chunks are appended in strict sequential order (the client always sends the next expected
/// index, per contracts/documents-api.md) into a single growing file per session, not one file
/// per chunk.
/// </summary>
public interface IResumableUploadStorage
{
    Task AppendChunkAsync(string sessionKey, Stream chunkContent, CancellationToken cancellationToken = default);

    /// <summary>Total bytes accumulated so far for this session — the source of truth for the next expected chunk index (data-model.md).</summary>
    Task<long> GetSizeAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>Opens the full accumulated content for reading (at completion time) — position reset to 0 before returning.</summary>
    Task<Stream> OpenReadAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>Idempotent — a no-op if the session's staged content is already gone.</summary>
    Task DeleteAsync(string sessionKey, CancellationToken cancellationToken = default);
}
