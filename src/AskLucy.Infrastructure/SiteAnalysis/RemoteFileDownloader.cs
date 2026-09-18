using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.SiteAnalysis;

/// <summary>The only Infrastructure implementation of <see cref="IRemoteFileDownloader"/> — plain outbound HTTP for a provider-hosted file URL, kept in its own named client so its timeout can diverge from every other outbound integration (matches the "Geocoding"/"Overpass" precedent in <c>DependencyInjection.cs</c>).</summary>
public sealed class RemoteFileDownloader(IHttpClientFactory httpClientFactory) : IRemoteFileDownloader
{
    public async Task<DownloadedFile> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient("SiteAnalysisImageDownload");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return new DownloadedFile(buffer, contentType);
    }
}
