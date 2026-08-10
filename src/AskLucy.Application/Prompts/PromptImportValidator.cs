namespace AskLucy.Application.Prompts;

/// <summary>One validation failure against a specific entry in an import file — <see cref="EntryIndex"/> is -1 for a file-level failure (e.g. an unrecognized schema version) not tied to any single prompt.</summary>
public sealed record PromptImportEntryError(int EntryIndex, string Message);

public sealed record PromptImportValidationResult(bool IsValid, IReadOnlyList<PromptImportEntryError> Errors)
{
    public static readonly PromptImportValidationResult Valid = new(true, []);
}

/// <summary>
/// Validates every entry in a <see cref="PromptExportFile"/> before any prompt is created (spec.md
/// FR-071, research.md Decision 13) — atomic, all-or-nothing: if any entry fails, the whole import
/// is rejected and nothing is persisted. Reuses <see cref="PromptContentAnalyzer"/> — the same
/// undeclared-placeholder/unreferenced-variable check <c>CreatePromptCommandHandler</c> already
/// enforces on save — so an imported entry cannot bypass a rule a hand-created prompt must satisfy.
/// A plain static class, not an injected service behind an interface, mirroring
/// <see cref="PromptContentAnalyzer"/>/<see cref="PromptVariableResolver"/>'s identical "no external
/// dependency, so no DI indirection" convention. A malformed <c>PromptType</c>/<c>PromptVariableType</c>
/// enum value never reaches this validator at all: ASP.NET Core's JSON model binding rejects it with
/// a 400 before the controller action even runs, since both enums are bound as strict enum types,
/// not raw strings.
/// </summary>
public static class PromptImportValidator
{
    private const int FileLevelErrorIndex = -1;

    public static PromptImportValidationResult Validate(PromptExportFile file)
    {
        var errors = new List<PromptImportEntryError>();

        if (file.SchemaVersion != PromptExportFile.CurrentSchemaVersion)
        {
            errors.Add(new PromptImportEntryError(
                FileLevelErrorIndex,
                $"Unrecognized export schema version {file.SchemaVersion} (expected {PromptExportFile.CurrentSchemaVersion})."));
            return new PromptImportValidationResult(false, errors);
        }

        if (file.Prompts.Count == 0)
        {
            errors.Add(new PromptImportEntryError(FileLevelErrorIndex, "The import file contains no prompts."));
            return new PromptImportValidationResult(false, errors);
        }

        for (var i = 0; i < file.Prompts.Count; i++)
        {
            errors.AddRange(ValidateEntry(i, file.Prompts[i]));
        }

        return errors.Count == 0 ? PromptImportValidationResult.Valid : new PromptImportValidationResult(false, errors);
    }

    private static IEnumerable<PromptImportEntryError> ValidateEntry(int index, PromptExportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            yield return new PromptImportEntryError(index, "A prompt name is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.UserInstructions))
        {
            yield return new PromptImportEntryError(index, "User instructions are required.");
        }

        foreach (var variable in entry.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                yield return new PromptImportEntryError(index, "Every variable must have a name.");
            }
        }

        var contentFields = new[]
        {
            entry.SystemInstructions, entry.DeveloperInstructions, entry.UserInstructions,
            entry.ContextText, entry.ExamplesText, entry.OutputInstructions, entry.Constraints,
        };
        var declaredNames = entry.Variables.Select(v => v.Name).ToList();
        var analysis = PromptContentAnalyzer.Analyze(contentFields, declaredNames);
        if (!analysis.IsValid)
        {
            if (analysis.UndeclaredPlaceholders.Count > 0)
            {
                yield return new PromptImportEntryError(
                    index, $"Undeclared placeholder(s) referenced in content: {string.Join(", ", analysis.UndeclaredPlaceholders)}.");
            }

            if (analysis.UnreferencedVariables.Count > 0)
            {
                yield return new PromptImportEntryError(
                    index, $"Declared variable(s) never referenced in content: {string.Join(", ", analysis.UnreferencedVariables)}.");
            }
        }
    }
}
