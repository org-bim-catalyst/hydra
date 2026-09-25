using System.Text.RegularExpressions;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// Redacts and bounds every stored failure reason (specs/074 research D8, FR-011–FR-013). Runs in
/// the ingestor, so no recording site can forget it. Callers already pass system prose; this is
/// defence in depth for the places a vendor message still leaks through.
/// </summary>
public static partial class FailureReasonSanitizer
{
    public const int MaxLength = 500;

    public const string Redacted = "[redacted]";

    public const string Withheld = "[reason withheld]";

    /// <summary>
    /// Input beyond this is dropped before redaction, so a multi-megabyte vendor body cannot make
    /// the patterns slow. Redaction can shrink the text a lot, so the token the bound cuts through
    /// is dropped too: a cut key would otherwise be too short to match and leak as a fragment.
    /// </summary>
    internal const int InputBound = 4000;

    private const int TimeoutMilliseconds = 100;

    public static string Sanitize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        try
        {
            var text = BoundInput(reason);
            text = Whitespace().Replace(text, " ").Trim();

            text = HeaderValue().Replace(text, "${name}${sep}" + Redacted);
            text = BearerToken().Replace(text, "Bearer " + Redacted);
            text = Jwt().Replace(text, Redacted);
            text = VendorKey().Replace(text, Redacted);
            text = ConnectionStringCredential().Replace(text, "${name}=" + Redacted);
            text = KeyValueSecret().Replace(text, "${name}${sep}" + Redacted);
            text = LongHexRun().Replace(text, Redacted);
            text = LongBase64Run().Replace(text, Redacted);

            return Truncate(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return Withheld;
        }
    }

    private static string BoundInput(string reason)
    {
        if (reason.Length <= InputBound)
        {
            return reason;
        }

        var bounded = reason[..InputBound];
        var lastSpace = bounded.LastIndexOfAny([' ', '\t', '\r', '\n']);
        return lastSpace > 0 ? bounded[..lastSpace] : string.Empty;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");

    [GeneratedRegex(@"\s+", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex Whitespace();

    // The value runs to the next quote, comma or brace: over-redacting prose after a header is safe.
    [GeneratedRegex(
        @"\b(?<name>proxy-authorization|authorization|set-cookie|cookie|x-api-key|xi-api-key|x-goog-api-key|api-key)(?<sep>""?\s*[:=]\s*""?)[^"",}]+",
        RegexOptions.IgnoreCase, TimeoutMilliseconds)]
    private static partial Regex HeaderValue();

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase, TimeoutMilliseconds)]
    private static partial Regex BearerToken();

    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]*)?|\b[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}",
        RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(?:sk-ant-|sk-|xi-)[A-Za-z0-9_-]{8,}|\bAIza[A-Za-z0-9_-]{20,}", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex VendorKey();

    [GeneratedRegex(@"\b(?<name>password|pwd|user\s?id|uid)\s*=\s*[^;""]*", RegexOptions.IgnoreCase, TimeoutMilliseconds)]
    private static partial Regex ConnectionStringCredential();

    [GeneratedRegex(
        @"\b(?<name>api[_-]?key|key|access[_-]?token|refresh[_-]?token|token|sig|signature|secret|client[_-]?secret|password|passwd)(?<sep>\s*[=:]\s*|""\s*:\s*"")[^\s&;,""'\[]+",
        RegexOptions.IgnoreCase, TimeoutMilliseconds)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"\b[0-9a-fA-F]{32,}\b", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex LongHexRun();

    // A digit is required so a long run of letters (a word, a path segment of words) is left alone.
    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])(?=[A-Za-z+/_-]*[0-9])[A-Za-z0-9+/_-]{32,}={0,2}", RegexOptions.None, TimeoutMilliseconds)]
    private static partial Regex LongBase64Run();
}
