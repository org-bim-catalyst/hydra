using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.SiteAnalysis;

/// <summary>
/// The only Infrastructure implementation of <see cref="IRemoteFileDownloader"/> — plain outbound
/// HTTP for a provider-hosted file URL. <paramref name="httpClientName"/> selects which named
/// client (own timeout/policy) each registration uses, matching the "Geocoding"/"Overpass"
/// precedent in <c>DependencyInjection.cs</c> — see its two registrations of this class for
/// "SiteAnalysisImageDownload" (default) and "ExternalProfilePictureDownload" (keyed).
/// </summary>
public sealed class RemoteFileDownloader(IHttpClientFactory httpClientFactory, string httpClientName) : IRemoteFileDownloader
{
    public async Task<DownloadedFile> DownloadAsync(Uri uri, long? maxContentLength = null, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(httpClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (maxContentLength is { } limit && response.Content.Headers.ContentLength is { } declaredLength && declaredLength > limit)
        {
            throw new InvalidOperationException($"Remote file at '{uri}' declares a Content-Length of {declaredLength} bytes, exceeding the {limit}-byte limit.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var buffer = new MemoryStream();
        await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await CopyWithLimitAsync(responseStream, buffer, maxContentLength, uri, cancellationToken);
        }

        buffer.Position = 0;

        return new DownloadedFile(buffer, contentType);
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long? maxContentLength, Uri uri, CancellationToken cancellationToken)
    {
        if (maxContentLength is not { } limit)
        {
            await source.CopyToAsync(destination, cancellationToken);
            return;
        }

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            totalRead += bytesRead;
            if (totalRead > limit)
            {
                throw new InvalidOperationException($"Remote file at '{uri}' exceeded the {limit}-byte limit while streaming.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }
}
