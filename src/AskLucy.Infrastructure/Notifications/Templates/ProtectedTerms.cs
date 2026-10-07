using System.Text.RegularExpressions;

namespace AskLucy.Infrastructure.Notifications.Templates;

/// <summary>
/// Brand names and acronyms that are never translated (specs/067, SC-016). Mirrors
/// <c>docs/localization/do-not-translate.md</c>, which is the canonical list; a test keeps the two
/// identical. In right-to-left email they are wrapped in <c>&lt;bdi&gt;</c> so the surrounding Arabic
/// can't reorder them.
/// </summary>
public static class ProtectedTerms
{
    public static IReadOnlyList<string> All { get; } =
    [
        "OpenAI",
        "Anthropic",
        "Gemini",
        "OpenRouter",
        "Ask Lucy",
        "API",
        "MCP",
        "SMTP",
        "2FA",
        "TOTP",
        "RAG",
        "OCR",
        "BIM",
        "PDF",
    ];

    // Longest first so a term that contains another is matched whole; letters or digits on either side
    // mean the term is only part of a longer word ("APIs", "RAGE"), which isn't the protected term.
    private static readonly Regex Pattern = new(
        @"(?<![\p{L}\p{N}])(" + string.Join('|', All.OrderByDescending(t => t.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Wraps each protected term in already HTML-encoded text with <c>&lt;bdi&gt;…&lt;/bdi&gt;</c>.
    /// Call it after encoding, so the only markup in the result is the wrapper itself.
    /// </summary>
    public static string WrapInBdi(string htmlEncodedText)
    {
        ArgumentNullException.ThrowIfNull(htmlEncodedText);
        return Pattern.Replace(htmlEncodedText, "<bdi>$1</bdi>");
    }
}
