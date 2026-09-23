using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AskLucy.Domain.CustomModels;

/// <summary>
/// specs/072 research D4, layer 1 of the SSRF containment. Parses a Hugging Face model repository
/// URL into its owner, repository and revision. The URL itself is <b>never fetched</b>: the server
/// builds its own requests from <see cref="RepositoryId"/> and the resolved commit, so everything
/// here is pure string work.
/// </summary>
/// <remarks>
/// Grammar: <c>http(s)://[www.]huggingface.co/{owner}/{repo}[/(tree|resolve|blob)/{revision}[/{file…}]]</c>.
/// A revision whose first segment is <c>refs</c> takes three segments (<c>refs/pr/3</c>); any other
/// revision takes one.
/// </remarks>
public sealed partial class HuggingFaceModelSource
{
    public const string DefaultRevision = "main";

    private const string NotHuggingFace = "The source must be a Hugging Face model URL on huggingface.co, for example https://huggingface.co/Supertone/supertonic-3.";

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase) { "huggingface.co", "www.huggingface.co" };

    private static readonly HashSet<string> NonModelSections = new(StringComparer.OrdinalIgnoreCase) { "datasets", "spaces" };

    private static readonly HashSet<string> RevisionSections = new(StringComparer.OrdinalIgnoreCase) { "tree", "resolve", "blob" };

    public string Owner { get; }

    public string Repository { get; }

    /// <summary><c>owner/repo</c> as typed. The job replaces it with Hugging Face's canonical casing (research D3).</summary>
    public string RepositoryId => $"{Owner}/{Repository}";

    /// <summary><see cref="DefaultRevision"/> when the URL names none.</summary>
    public string Revision { get; }

    /// <summary>The file a <c>/resolve/…</c> or <c>/blob/…</c> URL pointed at. The whole repository is still deployed; the UI says so.</summary>
    public string? IgnoredFilePath { get; }

    /// <summary>The repository name, or <see langword="null"/> when it wouldn't be a valid model name, so the admin must supply one (FR-002).</summary>
    public string? DerivedName { get; }

    /// <summary>The URL as submitted (trimmed). Display only.</summary>
    public string SourceUrl { get; }

    private HuggingFaceModelSource(string owner, string repository, string revision, string? ignoredFilePath, string sourceUrl)
    {
        Owner = owner;
        Repository = repository;
        Revision = revision;
        IgnoredFilePath = ignoredFilePath;
        SourceUrl = sourceUrl;
        DerivedName = CustomModel.IsValidName(repository) ? repository : null;
    }

    public static bool TryParse(string? raw, [NotNullWhen(true)] out HuggingFaceModelSource? source, [NotNullWhen(false)] out string? error)
    {
        source = null;
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "A source URL is required.";
            return false;
        }

        if (trimmed.Length > CustomModel.MaxSourceUrlLength)
        {
            error = $"The source URL must be at most {CustomModel.MaxSourceUrlLength} characters.";
            return false;
        }

        // Uri silently turns '\' into '/' and resolves dot segments, which would hide both from the
        // checks below — so they're refused on the raw text first.
        if (trimmed.Contains('\\') || HasWhitespace(trimmed))
        {
            error = NotHuggingFace;
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.HostNameType != UriHostNameType.Dns
            || !AllowedHosts.Contains(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            error = NotHuggingFace;
            return false;
        }

        if (!TryGetRawPathSegments(trimmed, out var segments))
        {
            error = "The source URL contains an empty, '.' or '..' path segment.";
            return false;
        }

        if (segments.Count > 0 && NonModelSections.Contains(segments[0]))
        {
            error = "The source is a Hugging Face dataset or Space, not a model repository.";
            return false;
        }

        if (segments.Count < 2)
        {
            error = "The source URL must name both the owner and the repository, for example https://huggingface.co/Supertone/supertonic-3.";
            return false;
        }

        var owner = segments[0];
        var repository = segments[1];
        if (!NamePattern().IsMatch(owner) || !NamePattern().IsMatch(repository))
        {
            error = "The owner and repository may contain only letters, digits, '.', '_' and '-', and must start with a letter or digit.";
            return false;
        }

        var revision = DefaultRevision;
        string? ignoredFilePath = null;
        if (segments.Count > 2)
        {
            if (!RevisionSections.Contains(segments[2]))
            {
                error = "The source URL must point at a model repository, optionally followed by /tree/<revision>.";
                return false;
            }

            var revisionSegmentCount = segments.Count > 3 && segments[3].Equals("refs", StringComparison.Ordinal) ? 3 : 1;
            if (segments.Count < 3 + revisionSegmentCount)
            {
                error = "The source URL names a revision section but no complete revision.";
                return false;
            }

            var revisionSegments = segments.Skip(3).Take(revisionSegmentCount).ToList();
            if (!revisionSegments.All(s => NamePattern().IsMatch(s)))
            {
                error = "The revision may contain only letters, digits, '.', '_' and '-'.";
                return false;
            }

            revision = string.Join('/', revisionSegments);
            if (revision.Length > CustomModel.MaxRevisionLength)
            {
                error = $"The revision must be at most {CustomModel.MaxRevisionLength} characters.";
                return false;
            }

            var fileSegments = segments.Skip(3 + revisionSegmentCount).ToList();
            if (fileSegments.Count > 0)
            {
                ignoredFilePath = string.Join('/', fileSegments);
            }
        }

        source = new HuggingFaceModelSource(owner, repository, revision, ignoredFilePath, trimmed);
        error = null;
        return true;
    }

    /// <summary>
    /// Splits the path exactly as typed (before <see cref="Uri"/> normalises it), percent-decoding
    /// each segment, and refuses empty, <c>.</c> and <c>..</c> segments. One trailing slash is allowed.
    /// </summary>
    private static bool TryGetRawPathSegments(string raw, out List<string> segments)
    {
        segments = [];
        var afterScheme = raw.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = raw.IndexOf('/', afterScheme);
        if (pathStart < 0)
        {
            return true;
        }

        var pathEnd = raw.IndexOfAny(['?', '#'], pathStart);
        var path = pathEnd < 0 ? raw[pathStart..] : raw[pathStart..pathEnd];
        path = path.Length > 1 && path.EndsWith('/') ? path[1..^1] : path.TrimStart('/');
        if (path.Length == 0)
        {
            return true;
        }

        foreach (var encoded in path.Split('/'))
        {
            var segment = Uri.UnescapeDataString(encoded);
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }

            segments.Add(segment);
        }

        return true;
    }

    private static bool HasWhitespace(string value) => value.Any(char.IsWhiteSpace);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
