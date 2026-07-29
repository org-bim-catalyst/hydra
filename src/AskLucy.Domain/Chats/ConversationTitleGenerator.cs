using System.Text.RegularExpressions;

namespace AskLucy.Domain.Chats;

/// <summary>
/// Derives a conversation title locally from its first message (FR-013), with no AI
/// provider call — the clarification session for specs/002-chat-history-management chose
/// zero added cost/latency/failure-mode over an AI-generated summary title (research.md
/// Topic 4). Strips Markdown/newlines, collapses whitespace, and truncates to 60
/// characters at a word boundary.
/// </summary>
public static partial class ConversationTitleGenerator
{
    private const int MaxLength = 60;

    public static string DeriveFrom(string firstMessageContent)
    {
        if (string.IsNullOrWhiteSpace(firstMessageContent))
        {
            return string.Empty;
        }

        var stripped = MarkdownPattern().Replace(firstMessageContent, " ");
        var collapsed = WhitespacePattern().Replace(stripped, " ").Trim();

        if (collapsed.Length <= MaxLength)
        {
            return collapsed;
        }

        var truncated = collapsed[..MaxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated.TrimEnd() + "…";
    }

    [GeneratedRegex(@"[*_`#>\[\]()!]")]
    private static partial Regex MarkdownPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
