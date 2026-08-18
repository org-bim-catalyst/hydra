using System.Text.RegularExpressions;

namespace AskLucy.Application.Prompts;

/// <summary>The result of scanning a prompt's content fields for <c>{{name}}</c> placeholders (spec.md FR-010, FR-014) against its declared <c>PromptVariable</c> names.</summary>
public sealed record PromptContentAnalysisResult(
    IReadOnlyList<string> ReferencedPlaceholders,
    IReadOnlyList<string> UndeclaredPlaceholders,
    IReadOnlyList<string> UnreferencedVariables)
{
    public bool IsValid => UndeclaredPlaceholders.Count == 0 && UnreferencedVariables.Count == 0;
}

/// <summary>
/// Pure, dependency-free placeholder-detection helper (research.md Decision 10) — no templating
/// library, just a compiled regex over <c>{{name}}</c> syntax. Used both to auto-detect variables
/// on save (FR-010) and to flag undeclared/unreferenced variables before a prompt can be saved as
/// ready-to-use or executed (FR-014).
/// </summary>
public static partial class PromptContentAnalyzer
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    internal static partial Regex PlaceholderPattern();

    public static IReadOnlyList<string> DetectPlaceholders(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        return PlaceholderPattern().Matches(content)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static PromptContentAnalysisResult Analyze(IEnumerable<string?> contentFields, IReadOnlyCollection<string> declaredVariableNames)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in contentFields)
        {
            foreach (var placeholder in DetectPlaceholders(field))
            {
                referenced.Add(placeholder);
            }
        }

        var declared = new HashSet<string>(declaredVariableNames, StringComparer.OrdinalIgnoreCase);

        var undeclaredPlaceholders = referenced.Where(name => !declared.Contains(name)).ToList();
        var unreferencedVariables = declared.Where(name => !referenced.Contains(name)).ToList();

        return new PromptContentAnalysisResult(referenced.ToList(), undeclaredPlaceholders, unreferencedVariables);
    }
}
