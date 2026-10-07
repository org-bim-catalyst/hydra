using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Templates;

/// <summary>Sample values for previews and test sends: never real data, and a link that can't resolve (contracts/admin-notifications-api.md).</summary>
public static class TemplateSamples
{
    public const string SampleLink = "https://example.invalid/sample-link";

    /// <summary>The variable that tells the renderer which version a <c>template.test</c> email should show.</summary>
    public const string TestVersionVariable = "templateVersionId";

    /// <summary>The type's variables with their fallback as sample, the sample link, and any caller-supplied values for declared names.</summary>
    public static Dictionary<string, string?> Build(NotificationTypeDefinition definition, IReadOnlyDictionary<string, string?>? overrides)
    {
        var values = definition.AllVariables.ToDictionary(v => v.Name, v => (string?)v.Fallback, StringComparer.Ordinal);
        if (overrides is not null)
        {
            foreach (var (name, value) in overrides)
            {
                if (values.ContainsKey(name) && !string.IsNullOrWhiteSpace(value))
                {
                    values[name] = value;
                }
            }
        }

        values[TemplateTokenParser.ActionUrlVariable] = SampleLink;
        return values;
    }
}
