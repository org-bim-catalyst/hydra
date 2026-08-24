namespace AskLucy.Application.Locations;

/// <summary>
/// specs/038-viewer-poi-zoom US2: detects explicit zoom intent from the user's free-text message
/// using case-insensitive keyword matching. No AI call — purely deterministic, zero latency.
/// </summary>
public interface IViewerZoomDetector
{
    /// <summary>
    /// Returns a <see cref="ViewerZoomCommand"/> when the message contains a recognized zoom keyword,
    /// or <see langword="null"/> when no zoom intent is detected.
    /// </summary>
    ViewerZoomCommand? Detect(string message);
}

/// <inheritdoc />
public sealed class ViewerZoomDetector : IViewerZoomDetector
{
    // Ordered with longer/more-specific phrases first to minimize false positives on substrings.
    private static readonly string[] InKeywords =
    [
        "zoom in", "get closer", "fly closer", "focus on", "come in", "move in", "zoomed in", "closer",
    ];

    private static readonly string[] OutKeywords =
    [
        "zoom out", "pull back", "fly back", "more context", "back up", "move out", "zoom back", "wider",
    ];

    public ViewerZoomCommand? Detect(string message)
    {
        var lower = message.ToLowerInvariant();
        foreach (var keyword in InKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
                return new ViewerZoomCommand("in");
        }
        foreach (var keyword in OutKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
                return new ViewerZoomCommand("out");
        }
        return null;
    }
}
