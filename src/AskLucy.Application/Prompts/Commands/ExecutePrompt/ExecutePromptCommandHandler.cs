using System.Runtime.CompilerServices;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Prompts.Authorization;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.ExecutePrompt;

/// <summary>
/// Orchestrates one prompt test execution: resolves/validates variables (FR-013 — blocks with a
/// per-variable `ValidationException` before any provider call, never a partial/best-effort
/// run), checks the target model's capabilities against the prompt's requirements (FR-004),
/// assembles the message list (system+developer instructions -> memory context -> RAG context ->
/// resolved user instructions, research.md Decision 14), and streams via the existing
/// <see cref="IAIProvider"/> abstraction — never a direct provider SDK call (FR-046). RAG/Memory
/// retrieval (User Story 6, FR-081/FR-082) reuses <see cref="IRagService"/>/<see cref="IMemoryService"/>
/// verbatim, passing a fresh per-attempt correlation id in the `userChatId` parameter slot
/// (research.md Decision 3 — confirmed a logging-only correlation id in both implementations,
/// never a foreign key, so it need not equal the <c>PromptExecution.Id</c> the caller later
/// persists, which does not exist yet at this point in the flow). Persistence
/// (<c>PromptExecution</c>/<c>PromptExecutionResult</c>) is the caller's job, mirroring
/// <c>SendChatMessageCommandHandler</c>'s split — see the trailing
/// <see cref="PromptStreamChunk.ResolvedVariableValuesJson"/>/<see cref="PromptStreamChunk.RetrievalOutcome"/>/
/// <see cref="PromptStreamChunk.MemoryOutcome"/> chunk and <c>RecordPromptExecutionCommand</c>.
/// </summary>
public sealed class ExecutePromptCommandHandler(
    IPromptRepository promptRepository,
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IRagService ragService,
    IMemoryService memoryService,
    ICurrentUserAccessor currentUser) : IStreamRequestHandler<ExecutePromptCommand, PromptStreamChunk>
{
    public async IAsyncEnumerable<PromptStreamChunk> Handle(
        ExecutePromptCommand request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var versionNumber = request.VersionNumber ?? prompt.CurrentVersionNumber;
        var version = await promptRepository.GetVersionAsync(prompt.Id, versionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Prompt version not found.");

        var resolution = PromptVariableResolver.ValidateAndResolve(version.Variables, request.VariableValues);
        if (!resolution.IsValid)
        {
            throw new ValidationException(resolution.Errors.Select(e => new ValidationFailure(e.VariableName, e.Message)));
        }

        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var modelCapabilities = new AIModelCapabilities(
            model.SupportsStreaming, model.SupportsVision, model.SupportsFunctionCalling, model.SupportsJsonMode,
            model.SupportsReasoning, model.SupportsEmbeddings, model.SupportsImageInput, model.SupportsImageOutput, model.SupportsAudio);
        var unmetRequirements = PromptCapabilityChecker.GetUnmetRequirements(prompt.RequiredCapabilities, modelCapabilities);
        if (unmetRequirements.Count > 0)
        {
            throw new DomainRuleViolationException(
                $"Model '{model.DisplayName}' does not support required capabilities: {string.Join(", ", unmetRequirements)}.");
        }

        var resolvedSystem = PromptVariableResolver.ResolveContent(version.SystemInstructions, resolution.ResolvedValues);
        var resolvedDeveloper = PromptVariableResolver.ResolveContent(version.DeveloperInstructions, resolution.ResolvedValues);
        var resolvedUser = PromptVariableResolver.ResolveContent(version.UserInstructions, resolution.ResolvedValues);

        var messages = new List<ChatMessage>();
        var combinedSystem = string.Join("\n\n", new[] { resolvedSystem, resolvedDeveloper }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(combinedSystem))
        {
            messages.Add(new ChatMessage(ChatRole.System, combinedSystem));
        }

        // research.md Decision 14: memory context, then RAG context, then the resolved user
        // instructions — each appended in that fixed order, never interpolated into another
        // segment's content, so instruction priority stays structurally distinguishable (FR-083/FR-092).
        var executionCorrelationId = Guid.CreateVersion7();

        MemoryRetrievalOutcome? memoryOutcome = null;
        if (request.UseMemoryContext)
        {
            memoryOutcome = await memoryService.RetrieveRelevantMemoriesAsync(
                userId, executionCorrelationId, projectId: null, resolvedUser, cancellationToken);

            if (memoryOutcome.Type == MemoryRetrievalOutcomeType.Found)
            {
                messages.Add(new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildMemorySystemMessage(memoryOutcome.ContextText!)));
            }
        }

        RagRetrievalOutcome? retrievalOutcome = null;
        if (request.UseRagContext && request.KnowledgeBaseIds is { Count: > 0 } knowledgeBaseIds)
        {
            retrievalOutcome = await ragService.RetrieveContextAsync(executionCorrelationId, resolvedUser, knowledgeBaseIds, cancellationToken);

            if (retrievalOutcome.Type == RagRetrievalOutcomeType.Grounded)
            {
                messages.Add(new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildRagSystemMessage(retrievalOutcome.ContextText!)));
            }
        }

        messages.Add(new ChatMessage(ChatRole.User, resolvedUser));

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, model.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new PromptStreamChunk(chunk.ContentDelta, chunk.Usage);
        }

        yield return new PromptStreamChunk(
            null, null, version.Id, JsonSerializer.Serialize(resolution.ResolvedValues), retrievalOutcome, memoryOutcome);
    }
}
