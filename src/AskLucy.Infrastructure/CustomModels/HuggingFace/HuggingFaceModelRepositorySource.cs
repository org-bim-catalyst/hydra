using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.CustomModels.HuggingFace;

/// <summary>
/// specs/072 research D3/D4. Reads public model repositories from Hugging Face's HTTP API. Every URL
/// is built here from the parsed repository id, revision and commit, with each part escaped; the URL
/// an admin typed is never fetched. Redirects are followed by hand so each hop can be checked
/// against the host allowlist (<see cref="HuggingFaceRedirectPolicy"/>), and the named client connects only to public addresses
/// (<see cref="SafeConnectCallback"/>).
/// </summary>
public sealed partial class HuggingFaceModelRepositorySource(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<CustomModelsOptions> options,
    TimeProvider timeProvider,
    ILogger<HuggingFaceModelRepositorySource> logger) : IModelRepositorySource
{
    public const string HttpClientName = "HuggingFace";

    private static readonly Uri BaseUri = new("https://huggingface.co/");

    /// <summary>Research D6/T068. Backoff between attempts on 429 and 5xx; <c>Retry-After</c> is honoured up to <see cref="MaxRetryAfter"/>.</summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(30)];

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    public async Task<ResolvedRevision> ResolveRevisionAsync(string repositoryId, string revision, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(BaseUri, $"api/models/{EscapeRepositoryId(repositoryId)}/revision/{Uri.EscapeDataString(revision)}");
        using var response = await SendAsync(uri, HttpCompletionOption.ResponseContentRead, repositoryId, cancellationToken);
        using var document = await ReadJsonAsync(response, repositoryId, cancellationToken);
        var root = document.RootElement;

        var sha = root.TryGetProperty("sha", out var shaElement) && shaElement.ValueKind == JsonValueKind.String ? shaElement.GetString() : null;
        if (sha is null || !CommitShaPattern().IsMatch(sha))
        {
            throw Unavailable($"Hugging Face didn't return a commit for '{repositoryId}' at '{revision}'.");
        }

        // Research D3 step 4: the canonical casing. Falls back to the typed id if the field is ever
        // missing; the column's case-insensitive collation keeps the rules working either way.
        var canonicalId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            && string.Equals(idElement.GetString(), repositoryId, StringComparison.OrdinalIgnoreCase)
            ? idElement.GetString()!
            : repositoryId;

        var isPrivate = root.TryGetProperty("private", out var privateElement) && privateElement.ValueKind == JsonValueKind.True;

        // "gated" is false, or the gating mode as a string ("auto" / "manual").
        var isGated = root.TryGetProperty("gated", out var gatedElement)
            && gatedElement.ValueKind is JsonValueKind.True or JsonValueKind.String;

        return new ResolvedRevision(canonicalId, sha, isPrivate, isGated);
    }

    public async Task<IReadOnlyList<ModelRepositoryFile>> ListFilesAsync(string repositoryId, string commitSha, CancellationToken cancellationToken = default)
    {
        var files = new List<ModelRepositoryFile>();
        Uri? next = new(BaseUri, $"api/models/{EscapeRepositoryId(repositoryId)}/tree/{Uri.EscapeDataString(commitSha)}?recursive=1");
        var pages = 0;
        while (next is not null)
        {
            if (++pages > 1000)
            {
                throw Unavailable($"Hugging Face's file listing for '{repositoryId}' didn't end.");
            }

            using var response = await SendAsync(next, HttpCompletionOption.ResponseContentRead, repositoryId, cancellationToken);
            using var document = await ReadJsonAsync(response, repositoryId, cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw Unavailable($"Hugging Face returned an unexpected file listing for '{repositoryId}'.");
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (ParseFile(entry, repositoryId) is { } file)
                {
                    files.Add(file);
                }
            }

            next = ParseNextLink(response, repositoryId);
        }

        return files;
    }

    public async Task DownloadAsync(
        string repositoryId,
        string commitSha,
        ModelRepositoryFile file,
        Stream destination,
        IProgress<long> progress,
        CancellationToken cancellationToken = default)
    {
        var escapedPath = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        var uri = new Uri(BaseUri, $"{EscapeRepositoryId(repositoryId)}/resolve/{Uri.EscapeDataString(commitSha)}/{escapedPath}");

        using var response = await SendAsync(uri, HttpCompletionOption.ResponseHeadersRead, repositoryId, cancellationToken);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var hash = file.Sha256 is not null
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        if (file.Sha256 is null)
        {
            // Git's blob id: sha1("blob {size}\0" + bytes).
            hash.AppendData(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"blob {file.Size}\0")));
        }

        var stallTimeout = TimeSpan.FromSeconds(options.CurrentValue.StallTimeoutSeconds);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long received = 0;
            while (true)
            {
                int read;
                // The watchdog restarts on every read, so it measures idle time, not total time.
                using (var idle = new CancellationTokenSource(stallTimeout, timeProvider))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token))
                {
                    try
                    {
                        read = await content.ReadAsync(buffer.AsMemory(), linked.Token);
                    }
                    catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new ModelRepositorySourceException(
                            ModelRepositorySourceFailureKind.DownloadStalled,
                            $"The download of '{file.Path}' stalled: no data arrived for {stallTimeout.TotalSeconds:0} seconds.");
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException)
                    {
                        throw new ModelRepositorySourceException(
                            ModelRepositorySourceFailureKind.Unavailable,
                            $"The connection to Hugging Face dropped while downloading '{file.Path}'.",
                            ex);
                    }
                }

                if (read == 0)
                {
                    break;
                }

                received += read;
                if (received > file.Size)
                {
                    throw Integrity(file);
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress.Report(received);
            }

            if (received != file.Size)
            {
                throw Integrity(file);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        var expected = file.Sha256 ?? file.GitBlobSha1;
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw Integrity(file);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, HttpCompletionOption completion, string repositoryId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await SendFollowingRedirectsAsync(uri, completion, repositoryId, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var status = response.StatusCode;
            var retryAfter = GetRetryAfter(response);
            response.Dispose();

            if ((status == HttpStatusCode.TooManyRequests || (int)status >= 500) && attempt < RetryDelays.Length)
            {
                var delay = retryAfter is { } header ? (header > MaxRetryAfter ? MaxRetryAfter : header) : RetryDelays[attempt];
                LogRetrying(logger, repositoryId, (int)status, delay.TotalSeconds, attempt + 1);
                await Task.Delay(delay, timeProvider, cancellationToken);
                continue;
            }

            throw status switch
            {
                // Unauthenticated requests for a missing repository get 401, not 404.
                HttpStatusCode.NotFound or HttpStatusCode.Unauthorized => new ModelRepositorySourceException(
                    ModelRepositorySourceFailureKind.NotFound,
                    $"'{repositoryId}' or the requested revision wasn't found on Hugging Face."),
                HttpStatusCode.Forbidden => new ModelRepositorySourceException(
                    ModelRepositorySourceFailureKind.GatedOrPrivate,
                    $"'{repositoryId}' is private or gated on Hugging Face. Only public repositories can be deployed."),
                _ => Unavailable($"Hugging Face answered {(int)status} for '{repositoryId}'. Try again later."),
            };
        }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri uri, HttpCompletionOption completion, string repositoryId, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = uri;
        for (var hop = 0; ; hop++)
        {
            if (!HuggingFaceRedirectPolicy.IsAllowed(current))
            {
                throw Unavailable($"Hugging Face redirected to an unexpected host ({current.Host}) for '{repositoryId}'. The download was refused.");
            }

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await client.SendAsync(request, completion, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new ModelRepositorySourceException(
                    ModelRepositorySourceFailureKind.Unavailable,
                    $"Hugging Face couldn't be reached for '{repositoryId}'. Try again later.",
                    ex);
            }

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= HuggingFaceRedirectPolicy.MaxRedirects)
            {
                throw Unavailable($"Hugging Face redirected too many times for '{repositoryId}'.");
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - timeProvider.GetUtcNow();
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string repositoryId, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new ModelRepositorySourceException(
                ModelRepositorySourceFailureKind.Unavailable,
                $"Hugging Face returned a response for '{repositoryId}' that couldn't be read.",
                ex);
        }
    }

    private static ModelRepositoryFile? ParseFile(JsonElement entry, string repositoryId)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "file")
        {
            return null;
        }

        if (!entry.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
            || !entry.TryGetProperty("oid", out var oid) || oid.ValueKind != JsonValueKind.String
            || !entry.TryGetProperty("size", out var size) || !size.TryGetInt64(out var sizeBytes))
        {
            throw Unavailable($"Hugging Face returned an incomplete file entry for '{repositoryId}'.");
        }

        string? sha256 = null;
        if (entry.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object)
        {
            if (!lfs.TryGetProperty("oid", out var lfsOid) || lfsOid.ValueKind != JsonValueKind.String)
            {
                throw Unavailable($"Hugging Face returned an incomplete large-file entry for '{repositoryId}'.");
            }

            sha256 = lfsOid.GetString();
            if (lfs.TryGetProperty("size", out var lfsSize) && lfsSize.TryGetInt64(out var lfsSizeBytes))
            {
                sizeBytes = lfsSizeBytes;
            }
        }

        return new ModelRepositoryFile(path.GetString()!, sizeBytes, sha256, oid.GetString()!);
    }

    /// <summary>Follows <c>Link: &lt;…&gt;; rel="next"</c>. The cursor URL goes through the same host check as a redirect.</summary>
    private static Uri? ParseNextLink(HttpResponseMessage response, string repositoryId)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var match in values.SelectMany(v => NextLinkPattern().Matches(v)))
        {
            var target = new Uri(BaseUri, match.Groups["url"].Value);
            if (!HuggingFaceRedirectPolicy.IsAllowed(target))
            {
                throw Unavailable($"Hugging Face's file listing for '{repositoryId}' pointed at an unexpected host.");
            }

            return target;
        }

        return null;
    }

    private static string EscapeRepositoryId(string repositoryId)
    {
        var parts = repositoryId.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ModelRepositorySourceException(ModelRepositorySourceFailureKind.NotFound, $"'{repositoryId}' isn't a Hugging Face repository id.");
        }

        return $"{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}";
    }

    private static ModelRepositorySourceException Unavailable(string message) =>
        new(ModelRepositorySourceFailureKind.Unavailable, message);

    private static ModelRepositorySourceException Integrity(ModelRepositoryFile file) =>
        new(ModelRepositorySourceFailureKind.IntegrityMismatch, $"'{file.Path}' didn't match the size or hash Hugging Face listed for it. The download was discarded.");

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaPattern();

    [GeneratedRegex("""<(?<url>[^>]+)>\s*;[^,]*rel\s*=\s*"?next"?""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NextLinkPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hugging Face answered {StatusCode} for {RepositoryId}; retrying in {DelaySeconds} s (attempt {Attempt})")]
    private static partial void LogRetrying(ILogger logger, string repositoryId, int statusCode, double delaySeconds, int attempt);
}
