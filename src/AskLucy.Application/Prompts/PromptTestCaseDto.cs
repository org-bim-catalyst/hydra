using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptTestCaseDto(
    Guid Id,
    string Name,
    string VariableValuesJson,
    string? ExpectedOutput,
    string? EvaluationCriteria,
    string ProviderKey,
    string ModelKey,
    Guid? SourceExecutionId,
    DateTime CreatedAtUtc)
{
    public static PromptTestCaseDto FromEntity(PromptTestCase testCase) => new(
        testCase.Id, testCase.Name, testCase.VariableValuesJson, testCase.ExpectedOutput, testCase.EvaluationCriteria,
        testCase.ProviderKey, testCase.ModelKey, testCase.SourceExecutionId, testCase.CreatedAtUtc);
}
