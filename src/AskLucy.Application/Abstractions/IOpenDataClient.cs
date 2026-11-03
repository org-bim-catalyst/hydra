using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Fetches content from an open data source over HTTP with retry and rate limiting.
/// Implemented in Infrastructure so the Application layer never depends on it directly.
/// </summary>
public interface IOpenDataClient
{
    /// <summary>
    /// Gets the underlying HTTP client.
    /// </summary>
    /// <returns>The HTTP client used for requests.</returns>
    HttpClient GetHttpClient();

    /// <summary>
    /// Sends a GET request to the specified URI with retry policy and rate limiting.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="retries">Number of retry attempts on failure.</param>
    /// <returns>The HTTP response message.</returns>
    Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken = default, int retries = 3);
}
