using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>A saved, reusable test scenario for a <see cref="Prompt"/> (spec.md FR-043).</summary>
public sealed class PromptTestCase : BaseEntity
{
    public Guid PromptId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string VariableValuesJson { get; private set; } = string.Empty;

    public string? ExpectedOutput { get; private set; }

    public string? EvaluationCriteria { get; private set; }

    public string ProviderKey { get; private set; } = string.Empty;

    public string ModelKey { get; private set; } = string.Empty;

    public Guid? SourceExecutionId { get; private set; }

    private PromptTestCase()
    {
        // Required by EF Core materialization.
    }

    public static PromptTestCase Create(
        Guid promptId, string name, string variableValuesJson, string? expectedOutput,
        string? evaluationCriteria, string providerKey, string modelKey, Guid? sourceExecutionId, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A test case name is required.");
        }

        return new PromptTestCase
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            Name = name.Trim(),
            VariableValuesJson = variableValuesJson,
            ExpectedOutput = expectedOutput,
            EvaluationCriteria = evaluationCriteria,
            ProviderKey = providerKey,
            ModelKey = modelKey,
            SourceExecutionId = sourceExecutionId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
