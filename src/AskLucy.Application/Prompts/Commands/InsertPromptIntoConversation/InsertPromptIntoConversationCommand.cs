using MediatR;

namespace AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;

/// <summary>
/// Inserts a saved prompt's current version into an active conversation as the next user message
/// (spec.md FR-080, User Story 5, contracts/prompt-conversation-integration-api.md). Always uses
/// the prompt's *current* version — there is no version selector on this contract, mirroring
/// <c>PreviewPromptQuery</c>.
/// </summary>
public sealed record InsertPromptIntoConversationCommand(
    Guid ChatId,
    Guid PromptId,
    IReadOnlyDictionary<string, string?> VariableValues) : IStreamRequest<PromptConversationInsertionStreamChunk>;
