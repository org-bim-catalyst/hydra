namespace AskLucy.Application.Abstractions;

public sealed record DownloadedFile(Stream Content, string? ContentType);

/// <summary>
/// Downloads a small, provider-hosted file by URL. specs/057-site-analysis-agent
/// contracts/schematic-image-prompt.md — <c>IAIProvider.GenerateImageAsync</c> returns a
/// transient, provider-hosted <see cref="Uri"/> that must be persisted as a platform
/// <c>Document</c> before it can be shown to a user (constitution &#167;8; the content block
/// schema rejects an external address outright). Application must not reference
/// <c>HttpClient</c> directly (constitution &#167;3) — the concrete HTTP call lives in
/// <c>Infrastructure</c>.
/// </summary>
public interface IRemoteFileDownloader
{
    /// <summary>
    /// When <paramref name="maxContentLength"/> is supplied, the download is aborted (throwing)
    /// as soon as either a declared <c>Content-Length</c> or the actual streamed byte count
    /// exceeds it — added for specs/062-external-login-profile-sync's provider-sourced picture
    /// fetch (research.md Decision 7), reusing this interface rather than adding a new one.
    /// </summary>
    Task<DownloadedFile> DownloadAsync(Uri uri, long? maxContentLength = null, CancellationToken cancellationToken = default);
}
