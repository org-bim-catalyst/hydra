using System.Globalization;
using System.Text.Json;
using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>Optional length/format/allowed-values rules parsed from <see cref="PromptVariable.ValidationRulesJson"/> (spec.md FR-011, FR-013).</summary>
public sealed record PromptVariableValidationRules(int? MinLength, int? MaxLength, string? Pattern, IReadOnlyList<string>? AllowedValues);

public sealed record PromptVariableValidationError(string VariableName, string Message);

public sealed record PromptVariableResolutionResult(
    bool IsValid, IReadOnlyList<PromptVariableValidationError> Errors, IReadOnlyDictionary<string, string> ResolvedValues)
{
    public static PromptVariableResolutionResult Failure(IReadOnlyList<PromptVariableValidationError> errors) =>
        new(false, errors, new Dictionary<string, string>());
}

/// <summary>
/// Validates supplied variable values against their <see cref="PromptVariable"/> definitions
/// (required/type/length/format/allowed-values, spec.md FR-013) and produces the resolved content
/// string. Used by both <c>ExecutePromptCommandHandler</c> (US2) and
/// <c>InsertPromptIntoConversationCommandHandler</c> (US5) — a required-but-missing/invalid value
/// always fails validation before any downstream call, per-variable, never a best-effort partial
/// resolution (SC-004).
/// </summary>
public static class PromptVariableResolver
{
    public static PromptVariableResolutionResult ValidateAndResolve(
        IReadOnlyCollection<PromptVariable> variables, IReadOnlyDictionary<string, string?> suppliedValues)
    {
        var errors = new List<PromptVariableValidationError>();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables.OrderBy(v => v.OrderIndex))
        {
            suppliedValues.TryGetValue(variable.Name, out var suppliedValue);
            var value = string.IsNullOrEmpty(suppliedValue) ? variable.DefaultValue : suppliedValue;

            if (string.IsNullOrEmpty(value))
            {
                if (variable.IsRequired)
                {
                    errors.Add(new PromptVariableValidationError(variable.Name, $"'{variable.Name}' is required."));
                }

                continue;
            }

            var typeError = ValidateType(variable, value);
            if (typeError is not null)
            {
                errors.Add(new PromptVariableValidationError(variable.Name, typeError));
                continue;
            }

            var ruleError = ValidateRules(variable, value);
            if (ruleError is not null)
            {
                errors.Add(new PromptVariableValidationError(variable.Name, ruleError));
                continue;
            }

            resolved[variable.Name] = value;
        }

        return errors.Count == 0
            ? new PromptVariableResolutionResult(true, errors, resolved)
            : PromptVariableResolutionResult.Failure(errors);
    }

    /// <summary>Preview-mode resolution (FR-005) — never blocks: a value not supplied falls back to <see cref="PromptVariable.DefaultValue"/>, then <see cref="PromptVariable.ExampleValue"/>, then a bracketed placeholder, so a preview always renders even for an incomplete draft.</summary>
    public static IReadOnlyDictionary<string, string> ResolveForPreview(
        IReadOnlyCollection<PromptVariable> variables, IReadOnlyDictionary<string, string?> suppliedValues)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            suppliedValues.TryGetValue(variable.Name, out var suppliedValue);
            resolved[variable.Name] = suppliedValue
                ?? variable.DefaultValue
                ?? variable.ExampleValue
                ?? $"[{variable.Name}]";
        }

        return resolved;
    }

    /// <summary>Substitutes every <c>{{name}}</c> placeholder in <paramref name="content"/> with its resolved value — the variable-value side of this pass; RAG/memory context is never interpolated here (research.md Decision 14).</summary>
    public static string ResolveContent(string? content, IReadOnlyDictionary<string, string> resolvedValues)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        return PromptContentAnalyzer.PlaceholderPattern().Replace(content, match =>
        {
            var name = match.Groups[1].Value;
            return resolvedValues.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    private static string? ValidateType(PromptVariable variable, string value) => variable.VariableType switch
    {
        PromptVariableType.Number => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? null : $"'{variable.Name}' must be a number.",
        PromptVariableType.Boolean => bool.TryParse(value, out _) ? null : $"'{variable.Name}' must be true or false.",
        PromptVariableType.Date => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? null : $"'{variable.Name}' must be a valid date.",
        PromptVariableType.Json => IsValidJson(value) ? null : $"'{variable.Name}' must be valid JSON.",
        _ => null,
    };

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ValidateRules(PromptVariable variable, string value)
    {
        var rules = ParseRules(variable.ValidationRulesJson);
        if (rules is null)
        {
            return null;
        }

        if (rules.MinLength is { } min && value.Length < min)
        {
            return $"'{variable.Name}' must be at least {min} characters.";
        }

        if (rules.MaxLength is { } max && value.Length > max)
        {
            return $"'{variable.Name}' must be at most {max} characters.";
        }

        if (rules.Pattern is { Length: > 0 } pattern && !System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
        {
            return $"'{variable.Name}' does not match the required format.";
        }

        if (rules.AllowedValues is { Count: > 0 } allowed && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{variable.Name}' must be one of: {string.Join(", ", allowed)}.";
        }

        return null;
    }

    private static PromptVariableValidationRules? ParseRules(string? validationRulesJson)
    {
        if (string.IsNullOrWhiteSpace(validationRulesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PromptVariableValidationRules>(validationRulesJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
