using System.Text;
using System.Text.RegularExpressions;

namespace AskLucy.Domain.Notifications;

/// <summary>Where a template field's text ends up. Only link fields may carry <c>{{ actionUrl }}</c> (FR-047).</summary>
public enum TemplateFieldKind
{
    Text,
    Link,
}

/// <summary>One problem found in a template field, with the offending token so the editor can point at it.</summary>
public sealed record TemplateTokenError(string Token, string Message);

/// <summary>The variables a field references, and any syntax errors in it.</summary>
public sealed record TemplateParseResult(IReadOnlyList<string> Variables, IReadOnlyList<TemplateTokenError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The logic-free <c>{{ name }}</c> token grammar (research R9, FR-041/FR-042). Pure: no I/O, no
/// expressions, no conditionals. Used on draft save, on publish, and by the renderer.
/// </summary>
public static partial class TemplateTokenParser
{
    public const string ActionUrlVariable = "actionUrl";

    [GeneratedRegex(@"\{\{(?<inner>[^{}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RawUrlRegex();

    [GeneratedRegex(@"<[a-zA-Z/]", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    /// <summary>Extracts the distinct variable names and reports malformed tokens and stray braces.</summary>
    public static TemplateParseResult Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TemplateParseResult([], []);
        }

        var variables = new List<string>();
        var errors = new List<TemplateTokenError>();

        foreach (Match match in TokenRegex().Matches(text))
        {
            var name = match.Groups["inner"].Value.Trim();
            if (!NameRegex().IsMatch(name))
            {
                errors.Add(new TemplateTokenError(match.Value, $"'{match.Value}' is not a valid variable token. Use {{{{ name }}}} with letters, digits and underscores."));
                continue;
            }

            if (!variables.Contains(name, StringComparer.Ordinal))
            {
                variables.Add(name);
            }
        }

        // Anything brace-like left once the well-formed tokens are removed is malformed.
        var remainder = TokenRegex().Replace(text, string.Empty);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            var stray = remainder.Contains("{{", StringComparison.Ordinal) ? "{{" : "}}";
            errors.Add(new TemplateTokenError(stray, $"Unmatched '{stray}'. Every token must be written as {{{{ name }}}}."));
        }

        return new TemplateParseResult(variables, errors);
    }

    /// <summary>
    /// Parses <paramref name="text"/> and checks it against a type's variables: every token must be
    /// declared, <c>actionUrl</c> is allowed only in link fields, and text fields may not contain raw
    /// URLs or HTML (FR-041, FR-047, FR-050).
    /// </summary>
    public static TemplateParseResult ValidateAgainst(string? text, Func<string, bool> isDeclared, TemplateFieldKind fieldKind = TemplateFieldKind.Text)
    {
        ArgumentNullException.ThrowIfNull(isDeclared);

        var parsed = Parse(text);
        var errors = new List<TemplateTokenError>(parsed.Errors);

        foreach (var variable in parsed.Variables)
        {
            if (string.Equals(variable, ActionUrlVariable, StringComparison.Ordinal))
            {
                if (fieldKind != TemplateFieldKind.Link)
                {
                    errors.Add(new TemplateTokenError(Token(variable), "The action link is added by the platform and can't be placed in a text field."));
                }

                continue;
            }

            if (!isDeclared(variable))
            {
                errors.Add(new TemplateTokenError(Token(variable), $"'{variable}' is not a variable this notification type provides."));
            }
        }

        if (fieldKind == TemplateFieldKind.Text && ContainsRawUrlOrHtml(text))
        {
            errors.Add(new TemplateTokenError(string.Empty, "Links and HTML aren't allowed in template text. Links come only from the action button."));
        }

        return new TemplateParseResult(parsed.Variables, errors);
    }

    public static bool ContainsRawUrlOrHtml(string? text) =>
        !string.IsNullOrEmpty(text) && (RawUrlRegex().IsMatch(text) || HtmlTagRegex().IsMatch(text));

    /// <summary>
    /// Replaces each well-formed token with <paramref name="valueFor"/>'s result. The caller owns
    /// escaping for the output context (R9); malformed text is returned unchanged around the tokens.
    /// </summary>
    public static string Substitute(string? text, Func<string, string> valueFor)
    {
        ArgumentNullException.ThrowIfNull(valueFor);
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var last = 0;
        foreach (Match match in TokenRegex().Matches(text))
        {
            var name = match.Groups["inner"].Value.Trim();
            if (!NameRegex().IsMatch(name))
            {
                continue;
            }

            builder.Append(text, last, match.Index - last);
            builder.Append(valueFor(name));
            last = match.Index + match.Length;
        }

        builder.Append(text, last, text.Length - last);
        return builder.ToString();
    }

    private static string Token(string name) => $"{{{{ {name} }}}}";
}
