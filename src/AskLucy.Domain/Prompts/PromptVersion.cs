using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>Content + model-settings the version was saved with (spec.md FR-030-FR-031) — kept as a single parameter object so <see cref="Prompt"/>'s factory/mutator signatures stay readable, mirroring <c>AIModelCapabilities</c>'s grouping role.</summary>
public sealed record PromptContentSnapshot(
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    string? ProviderKey,
    string? ModelKey,
    decimal? Temperature,
    int? MaxOutputTokens,
    bool StructuredOutputRequested);

/// <summary>
/// An immutable snapshot of a <see cref="Prompt"/>'s content, variables, and model settings at a
/// point in time (spec.md FR-030-FR-033, data-model.md). Created only via
/// <see cref="Prompt.CreateVersionSnapshot"/> — never constructed directly by Application-layer
/// code. Append-only: no update/delete methods.
/// </summary>
public sealed class PromptVersion : BaseEntity
{
    private readonly List<PromptVariable> _variables = [];

    public Guid PromptId { get; private set; }

    public int VersionNumber { get; private set; }

    public string? SystemInstructions { get; private set; }

    public string? DeveloperInstructions { get; private set; }

    public string UserInstructions { get; private set; } = string.Empty;

    public string? ContextText { get; private set; }

    public string? ExamplesText { get; private set; }

    public string? OutputInstructions { get; private set; }

    public string? Constraints { get; private set; }

    public string? ProviderKey { get; private set; }

    public string? ModelKey { get; private set; }

    public decimal? Temperature { get; private set; }

    public int? MaxOutputTokens { get; private set; }

    public bool StructuredOutputRequested { get; private set; }

    public string? ChangeDescription { get; private set; }

    public IReadOnlyCollection<PromptVariable> Variables => _variables;

    private PromptVersion()
    {
        // Required by EF Core materialization.
    }

    internal static PromptVersion Create(
        Guid promptId,
        int versionNumber,
        PromptContentSnapshot content,
        IReadOnlyList<PromptVariableDefinition> variables,
        string? changeDescription,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(content.UserInstructions))
        {
            throw new DomainRuleViolationException("User instructions are required.");
        }

        var version = new PromptVersion
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            VersionNumber = versionNumber,
            SystemInstructions = content.SystemInstructions,
            DeveloperInstructions = content.DeveloperInstructions,
            UserInstructions = content.UserInstructions.Trim(),
            ContextText = content.ContextText,
            ExamplesText = content.ExamplesText,
            OutputInstructions = content.OutputInstructions,
            Constraints = content.Constraints,
            ProviderKey = content.ProviderKey,
            ModelKey = content.ModelKey,
            Temperature = content.Temperature,
            MaxOutputTokens = content.MaxOutputTokens,
            StructuredOutputRequested = content.StructuredOutputRequested,
            ChangeDescription = changeDescription,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in variables)
        {
            if (!seenNames.Add(definition.Name.Trim()))
            {
                throw new DomainRuleViolationException($"Variable '{definition.Name}' is defined more than once.");
            }

            version._variables.Add(PromptVariable.Create(version.Id, definition, actor));
        }

        return version;
    }
}
