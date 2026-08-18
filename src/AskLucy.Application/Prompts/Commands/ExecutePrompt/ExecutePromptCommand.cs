using AskLucy.Application.Ai;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.ExecutePrompt;

/// <summary>Executes a prompt from the Testing Workspace, streaming the response (spec.md FR-040-FR-046, contracts/prompt-execution-api.md).</summary>
public sealed record ExecutePromptCommand(
    Guid PromptId,
    int? VersionNumber,
    IReadOnlyDictionary<string, string?> VariableValues,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    bool UseRagContext,
    IReadOnlyList<Guid>? KnowledgeBaseIds,
    bool UseMemoryContext) : IStreamRequest<PromptStreamChunk>;
