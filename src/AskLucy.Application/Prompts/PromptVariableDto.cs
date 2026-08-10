using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>A variable definition as returned/accepted by the Prompt API (spec.md FR-010-FR-012, contracts/prompts-api.md).</summary>
public sealed record PromptVariableDto(
    string Name,
    string? Description,
    PromptVariableType Type,
    bool IsRequired,
    string? DefaultValue,
    string? ExampleValue,
    string? ValidationRulesJson,
    int OrderIndex)
{
    public static PromptVariableDto FromEntity(PromptVariable variable) => new(
        variable.Name, variable.Description, variable.VariableType, variable.IsRequired,
        variable.DefaultValue, variable.ExampleValue, variable.ValidationRulesJson, variable.OrderIndex);

    public PromptVariableDefinition ToDefinition() => new(
        Name, Description, Type, IsRequired, DefaultValue, ExampleValue, ValidationRulesJson, OrderIndex);
}
