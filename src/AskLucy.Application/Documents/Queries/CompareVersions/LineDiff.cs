namespace AskLucy.Application.Documents.Queries.CompareVersions;

/// <summary>
/// A minimal, self-contained unified-diff-style line comparator (classic LCS dynamic
/// programming) — FR-042 only requires "at minimum" a comparison, not a specific diff format, so
/// this avoids adding a NuGet dependency (e.g. DiffPlex) for what a small BCL-only algorithm
/// already covers. <c>+</c>/<c>-</c>/(unchanged, unprefixed) per line, mirroring `diff -u`'s
/// convention closely enough to be immediately readable without a legend.
/// </summary>
internal static class LineDiff
{
    /// <summary>The LCS table is O(n*m) in both time and memory — capped so a pathologically large document can't exhaust memory; the caller sees a truncation note instead of a crash.</summary>
    private const int MaxLinesPerSide = 4000;

    public static string Compute(string? fromText, string? toText)
    {
        var fromLines = SplitLines(fromText, out var fromTruncated);
        var toLines = SplitLines(toText, out var toTruncated);

        var lcs = new int[fromLines.Length + 1, toLines.Length + 1];
        for (var i = 1; i <= fromLines.Length; i++)
        {
            for (var j = 1; j <= toLines.Length; j++)
            {
                lcs[i, j] = fromLines[i - 1] == toLines[j - 1]
                    ? lcs[i - 1, j - 1] + 1
                    : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }

        var result = Backtrack(fromLines, toLines, lcs);

        if (fromTruncated || toTruncated)
        {
            result.Add($"… truncated at {MaxLinesPerSide} lines per side …");
        }

        return string.Join('\n', result);
    }

    private static string[] SplitLines(string? text, out bool truncated)
    {
        var lines = (text ?? string.Empty).Split('\n');
        truncated = lines.Length > MaxLinesPerSide;
        return truncated ? lines[..MaxLinesPerSide] : lines;
    }

    /// <summary>
    /// Walks backward from the bottom-right of the LCS table to (0,0) — iterative, not recursive,
    /// so the call stack never grows with input size (a naive top-down recursive backtrack risks
    /// a stack overflow for a document with thousands of lines). Lines are produced in reverse
    /// order and flipped once at the end.
    /// </summary>
    private static List<string> Backtrack(string[] from, string[] to, int[,] lcs)
    {
        var reversed = new List<string>();
        var i = from.Length;
        var j = to.Length;

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && from[i - 1] == to[j - 1])
            {
                reversed.Add(from[i - 1]);
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                reversed.Add("+ " + to[j - 1]);
                j--;
            }
            else
            {
                reversed.Add("- " + from[i - 1]);
                i--;
            }
        }

        reversed.Reverse();
        return reversed;
    }
}
