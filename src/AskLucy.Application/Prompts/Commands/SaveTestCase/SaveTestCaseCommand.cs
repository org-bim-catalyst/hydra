using MediatR;

namespace AskLucy.Application.Prompts.Commands.SaveTestCase;

/// <summary>Saves a reusable test scenario, optionally captured from a completed execution (spec.md FR-043).</summary>
public sealed record SaveTestCaseCommand(
    Guid PromptId,
    string Name,
    string VariableValuesJson,
    string? ExpectedOutput,
    string? EvaluationCriteria,
    string ProviderKey,
    string ModelKey,
    Guid? SourceExecutionId) : IRequest<PromptTestCaseDto>;
