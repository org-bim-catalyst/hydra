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
    Task<DownloadedFile> DownloadAsync(Uri uri, CancellationToken cancellationToken = default);
}
