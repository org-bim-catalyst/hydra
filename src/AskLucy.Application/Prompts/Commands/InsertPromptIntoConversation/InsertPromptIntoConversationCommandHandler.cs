using System.Runtime.CompilerServices;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Application.Prompts.Commands.RecordPromptExecution;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;

/// <summary>
/// Orchestrates one prompt-into-conversation insertion (spec.md FR-080, research.md Decision 4):
/// resolves/validates the prompt's variables (FR-013 — blocks with a <see cref="ValidationException"/>
/// before anything is sent), checks the conversation's currently-selected model against the
/// prompt's required capabilities (FR-004), persists the resolved text as the conversation's next
/// user message, delegates to the existing <see cref="SendChatMessageCommand"/> unchanged for
/// provider/model selection, RAG, memory, and streaming (via <see cref="ISender"/>, the same
/// established delegation pattern <c>StreamVoiceReplyCommandHandler</c> already uses), then persists
/// the assistant reply and records a <see cref="PromptExecution"/> row on success only. Deliberately
/// has no try/catch around the delegated stream (an iterator cannot wrap a `yield` in try/catch,
/// same constraint documented on <c>ExecutePromptCommandHandler</c>) — a provider failure propagates
/// uncaught, so neither the assistant message nor the <c>PromptExecution</c> row is ever written on
/// failure, matching contracts/prompt-conversation-integration-api.md's failure posture exactly.
/// </summary>
public sealed class InsertPromptIntoConversationCommandHandler(
    IPromptRepository promptRepository,
    IUserChatRepository userChatRepository,
    IMessageRepository messageRepository,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    ISender mediator,
    ICurrentUserAccessor currentUser) : IStreamRequestHandler<InsertPromptIntoConversationCommand, PromptConversationInsertionStreamChunk>
{
    public async IAsyncEnumerable<PromptConversationInsertionStreamChunk> Handle(
        InsertPromptIntoConversationCommand request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await userChatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        if (chat.ProviderId is null || chat.ModelId is null)
        {
            throw new DomainRuleViolationException("Select a provider and model for this conversation before inserting a prompt.");
        }

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var version = await promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var resolution = PromptVariableResolver.ValidateAndResolve(version.Variables, request.VariableValues);
        if (!resolution.IsValid)
        {
            throw new ValidationException(resolution.Errors.Select(e => new ValidationFailure(e.VariableName, e.Message)));
        }

        var provider = await providerRepository.GetByIdAsync(chat.ProviderId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(chat.ModelId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var modelCapabilities = new AIModelCapabilities(
            model.SupportsStreaming, model.SupportsVision, model.SupportsFunctionCalling, model.SupportsJsonMode,
            model.SupportsReasoning, model.SupportsEmbeddings, model.SupportsImageInput, model.SupportsImageOutput, model.SupportsAudio);
        var unmetRequirements = PromptCapabilityChecker.GetUnmetRequirements(prompt.RequiredCapabilities, modelCapabilities);
        if (unmetRequirements.Count > 0)
        {
            throw new DomainRuleViolationException(
                $"This conversation's model '{model.DisplayName}' does not support required capabilities: {string.Join(", ", unmetRequirements)}.");
        }

        var resolvedSystem = PromptVariableResolver.ResolveContent(version.SystemInstructions, resolution.ResolvedValues);
        var resolvedDeveloper = PromptVariableResolver.ResolveContent(version.DeveloperInstructions, resolution.ResolvedValues);
        var resolvedUser = PromptVariableResolver.ResolveContent(version.UserInstructions, resolution.ResolvedValues);

        var priorMessages = await messageRepository.ListByChatIdAsync(request.ChatId, cancellationToken);
        var messages = priorMessages
            .Select(m => new ChatMessageDto(m.Role == MessageRole.Assistant ? "assistant" : "user", m.Content))
            .ToList();

        var combinedSystem = string.Join("\n\n", new[] { resolvedSystem, resolvedDeveloper }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(combinedSystem))
        {
            messages.Insert(0, new ChatMessageDto("system", combinedSystem));
        }

        messages.Add(new ChatMessageDto("user", resolvedUser));

        await mediator.Send(new AppendMessageCommand(request.ChatId, MessageRole.User, MessageKind.Text, resolvedUser, null), cancellationToken);

        var generationParameters = string.IsNullOrEmpty(chat.GenerationParametersJson)
            ? null
            : JsonSerializer.Deserialize<GenerationParametersDto>(chat.GenerationParametersJson);

        var assistantContent = new System.Text.StringBuilder();
        ChatUsage? finalUsage = null;

        await foreach (var chunk in mediator.CreateStream(
            new SendChatMessageCommand(request.ChatId, messages, chat.ProviderId.Value, chat.ModelId.Value, generationParameters),
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                assistantContent.Append(chunk.ContentDelta);
            }

            if (chunk.Usage is not null)
            {
                finalUsage = chunk.Usage;
            }

            yield return new PromptConversationInsertionStreamChunk(chunk.ContentDelta, chunk.Usage, chunk.RetrievalOutcome, chunk.MemoryOutcome);
        }

        var estimatedCostUsd = CostEstimator.Estimate(model.Pricing, finalUsage?.InputTokenCount, finalUsage?.OutputTokenCount);
        var generationParametersJson = generationParameters is null ? null : JsonSerializer.Serialize(generationParameters);

        var assistantMessage = await mediator.Send(
            new AppendMessageCommand(
                request.ChatId, MessageRole.Assistant, MessageKind.Text, assistantContent.ToString(), null,
                Provider: provider.DisplayName, Model: model.ModelKey, GenerationParametersJson: generationParametersJson,
                InputTokenCount: finalUsage?.InputTokenCount, OutputTokenCount: finalUsage?.OutputTokenCount,
                CachedTokenCount: finalUsage?.CachedTokenCount, ReasoningTokenCount: finalUsage?.ReasoningTokenCount,
                LatencyMs: finalUsage?.LatencyMs, EstimatedCostUsd: estimatedCostUsd),
            cancellationToken);

        await mediator.Send(
            new RecordPromptExecutionCommand(
                prompt.Id, version.Id, PromptExecutionOrigin.ConversationInsertion, model.Id, provider.ProviderKey, model.ModelKey,
                (decimal?)generationParameters?.Temperature, generationParameters?.MaxTokens, generationParameters?.JsonMode ?? false,
                JsonSerializer.Serialize(resolution.ResolvedValues), RequestedRagContext: false, RequestedMemoryContext: false,
                Outcome: PromptExecutionOutcome.Success, ErrorDetail: null, LatencyMs: finalUsage?.LatencyMs,
                OutputText: assistantContent.ToString(), InputTokenCount: finalUsage?.InputTokenCount, OutputTokenCount: finalUsage?.OutputTokenCount,
                RagCitationsJson: null, MemoryReferencesJson: null, ResultMessageId: assistantMessage.Id),
            cancellationToken);

        yield return new PromptConversationInsertionStreamChunk(
            null, null, null, null, version.Id, JsonSerializer.Serialize(resolution.ResolvedValues));
    }
}
