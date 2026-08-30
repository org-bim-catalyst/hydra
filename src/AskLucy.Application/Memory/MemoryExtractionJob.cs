using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using Hangfire;
using Microsoft.Extensions.Logging;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory;

/// <summary>
/// Implements <see cref="IMemoryExtractionJob"/> (spec.md FR-006, FR-006a, FR-006b, FR-008;
/// research.md Decisions 6/7/8). See <see cref="IMemoryExtractionJob"/>'s doc comment for why
/// this concrete type lives in <c>AskLucy.Application</c>.
///
/// <para>Each extracted candidate is its own business transaction (constitution §3): the memory
/// row, its audit log entry, and anything <see cref="IMemoryConflictDetectionService"/> adds all
/// commit together via one <see cref="IUnitOfWork.SaveChangesAsync"/> call, then — only for a
/// candidate that survived (wasn't merged away) — its embedding is added, saved (the row must
/// exist before the raw-SQL vector <c>UPDATE</c>, mirroring <c>IndexingOrchestrator</c>'s
/// identical ordering), and upserted into <see cref="IMemoryVectorStore"/>.</para>
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
public sealed class MemoryExtractionJob(
    IUserChatRepository userChatRepository,
    IMessageRepository messageRepository,
    IMemoryRepository memoryRepository,
    IMemoryPreferenceRepository preferenceRepository,
    IMemoryApprovalRepository approvalRepository,
    IMemoryAuditLogRepository auditLogRepository,
    IMemoryEmbeddingRepository memoryEmbeddingRepository,
    IMemoryConflictDetectionService conflictDetectionService,
    IMemoryNotifier notifier,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IMemoryVectorStore vectorStore,
    IAIProviderRepository aiProviderRepository,
    IAIModelRepository aiModelRepository,
    IAIProviderResolver aiProviderResolver,
    AiCapabilityProviderResolver capabilityProviderResolver,
    IUnitOfWork unitOfWork,
    ILogger<MemoryExtractionJob> logger) : IMemoryExtractionJob
{
    private const string SystemActor = "system:memory-extraction";

    /// <summary>Explicit statements ("remember that...", direct preference declarations) get a higher default importance than facts merely inferred from context — the extraction prompt doesn't ask the model for a separate importance score (tasks.md T032's content/category/isExplicit/isSensitive/confidence shape).</summary>
    private const decimal ExplicitImportance = 0.8m;

    private const decimal InferredImportance = 0.5m;

    /// <summary>v1 — versioned per constitution §9 ("Prompt engineering... reviewed like code, testable in isolation").</summary>
    private const string ExtractionSystemPromptV1 =
        "You read a snippet of a conversation between a user and an AI assistant and identify " +
        "durable facts or preferences about the USER worth remembering for future conversations — " +
        "not conversational filler, not the assistant's own statements. For each one found, classify " +
        "it into exactly one category: \"UserPreference\" (a stated preference — language, tools, " +
        "style), \"PersonalFact\" (a fact about the user or their work/company), \"ProjectContext\" " +
        "(a fact tied to the specific project/task at hand), or \"ConversationDerived\" (something " +
        "inferred from context rather than stated outright). Mark isSensitive true only for " +
        "health/financial/legal/other clearly sensitive personal information. Respond with ONLY a " +
        "single JSON array (no markdown, no commentary) — empty if nothing is worth remembering — " +
        "each entry shaped exactly as {\"content\":\"<short first-person-neutral statement>\"," +
        "\"category\":\"<one of the four above>\",\"isExplicit\":<bool, true if the user stated it " +
        "directly rather than it being merely implied>,\"isSensitive\":<bool>,\"confidence\":<0..1>}.";

    public async Task RunAsync(Guid userChatId, CancellationToken cancellationToken = default)
    {
        var userChat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (userChat is null)
        {
            return; // Conversation was deleted before this job ran — nothing to analyze.
        }

        var preference = await preferenceRepository.GetByUserIdAsync(userChat.UserId, cancellationToken);
        if (preference is not null && !preference.MemoryEnabled)
        {
            return;
        }

        var candidates = await ExtractCandidatesAsync(userChat, cancellationToken);

        foreach (var candidate in candidates)
        {
            await ProcessCandidateAsync(userChat, candidate, cancellationToken);
        }

        userChat.MarkMemoryAnalyzed();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessCandidateAsync(UserChat userChat, ExtractedCandidate candidate, CancellationToken cancellationToken)
    {
        var category = candidate.Category;

        var categoryPreference = await preferenceRepository.GetCategoryPreferenceAsync(userChat.UserId, category, cancellationToken);
        if (categoryPreference is null)
        {
            categoryPreference = MemoryCategoryPreference.CreateDefault(userChat.UserId, category, SystemActor);
            preferenceRepository.AddCategoryPreference(categoryPreference);
        }

        if (categoryPreference.ApprovalMode == MemoryApprovalMode.Disabled || !categoryPreference.IsEnabled)
        {
            return;
        }

        // Edge case ("same fact stated many times", spec.md) — an exact restatement reinforces the
        // existing memory instead of creating a duplicate. Paraphrased restatements fall through to
        // IMemoryConflictDetectionService's AI-based comparison below, which may classify them as a
        // conflict rather than a clean reinforcement — a documented, acceptable simplification.
        var existingInCategory = await memoryRepository.GetActiveByCategoryAsync(userChat.UserId, userChat.ProjectId, category, cancellationToken);
        var exactMatch = existingInCategory.FirstOrDefault(m =>
            string.Equals(m.Content.Trim(), candidate.Content.Trim(), StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            exactMatch.Reinforce(SystemActor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var importance = candidate.IsExplicit ? ExplicitImportance : InferredImportance;

        var memory = MemoryEntity.CreateCandidate(
            userChat.UserId, userChat.ProjectId, category, candidate.Content, MemorySourceType.PassiveConversationAnalysis,
            userChat.Id, importance, candidate.Confidence, candidate.IsSensitive, categoryPreference.ApprovalMode, SystemActor);
        memoryRepository.Add(memory);

        // spec.md FR-007's "source disclosed" requirement — a row exists from the moment a
        // candidate is created, whichever way it resolves: pending for manual review, or
        // already-decided (auto-approved) for automatic mode, so "who/what approved this" is
        // always answerable, never inferred from the memory's own State alone.
        approvalRepository.Add(memory.State == MemoryLifecycleState.PendingApproval
            ? MemoryApproval.CreatePending(memory.Id, SystemActor)
            : MemoryApproval.CreateDecided(memory.Id, MemoryApprovalDecision.Approved, SystemActor));

        auditLogRepository.Add(MemoryAuditLog.Create(
            memory.Id, userChat.UserId, SystemActor, MemoryAuditAction.Created,
            JsonSerializer.Serialize(new { source = "extraction", category = category.ToString() }), SystemActor));

        var consumedByMerge = await conflictDetectionService.DetectAndResolveAsync(memory, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (consumedByMerge)
        {
            return; // Merged into an existing memory — no embedding of its own to create.
        }

        // spec.md FR-006a — a low-noise signal specifically for the "created without review"
        // case (Automatic mode); Manual/sensitive-forced candidates surface through the
        // approval queue instead, and an ambiguous-conflict candidate already got its own
        // notification from IMemoryConflictDetectionService above.
        if (memory.State == MemoryLifecycleState.Active)
        {
            await notifier.NotifyAsync(
                userChat.UserId, memory.Id, MemoryNotificationEventType.AutoApproved,
                "Lucy automatically remembered something new.", cancellationToken);
        }

        await EmbedAndUpsertAsync(memory, cancellationToken);
    }

    private async Task EmbedAndUpsertAsync(MemoryEntity memory, CancellationToken cancellationToken)
    {
        var provider = await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken)
            ?? throw new InvalidOperationException("No default embedding provider is configured.");
        var embeddingService = embeddingServiceResolver.Resolve(provider.Vendor);
        var embeddingResult = await embeddingService.EmbedAsync(memory.Content, cancellationToken);

        var embedding = MemoryEmbedding.Create(memory.Id, provider.Id, embeddingResult.Vector, SystemActor);
        memoryEmbeddingRepository.Add(embedding);
        await unitOfWork.SaveChangesAsync(cancellationToken); // The row must exist before the raw-SQL vector UPDATE below.

        await vectorStore.UpsertAsync(memory.Id, embedding.Id, embeddingResult.Vector, cancellationToken);
    }

    private async Task<IReadOnlyList<ExtractedCandidate>> ExtractCandidatesAsync(UserChat userChat, CancellationToken cancellationToken)
    {
        var messages = await messageRepository.ListByChatIdAsync(userChat.Id, cancellationToken);

        var newMessages = messages
            .Where(m => userChat.LastMemoryAnalyzedAtUtc is null || m.CreatedAtUtc > userChat.LastMemoryAnalyzedAtUtc)
            .Where(m => m.Kind == MessageKind.Text)
            .OrderBy(m => m.CreatedAtUtc)
            .ToList();

        if (newMessages.Count == 0)
        {
            return [];
        }

        var transcript = string.Join(
            Environment.NewLine, newMessages.Select(m => $"{m.Role}: {m.Content}"));

        try
        {
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.MemoryExtraction, cancellationToken);
            var provider = await aiProviderRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException("Provider not found.");
            var model = await aiModelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
                ?? throw new KeyNotFoundException("Model not found.");
            var aiProvider = aiProviderResolver.Resolve(provider.ProviderKey);

            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, ExtractionSystemPromptV1),
                new(ChatRole.User, $"Conversation snippet:\n{transcript}"),
            };

            var completion = await aiProvider.ChatAsync(chatMessages, model.ModelKey, parameters: null, cancellationToken);

            return ParseCandidates(completion.Content);
        }
        catch (Exception ex) when (ex is JsonException)
        {
            MemoryExtractionJobLog.ExtractionParseFailed(logger, userChat.Id, ex);
            return [];
        }
        // Any other exception (provider outage, auth failure, rate limit) propagates so Hangfire's
        // [AutomaticRetry] above can retry the whole run (FR-006b) — only a malformed response is
        // treated as "nothing found" rather than retried, since retrying won't fix bad JSON.
    }

    private static List<ExtractedCandidate> ParseCandidates(string content)
    {
        var parsed = JsonSerializer.Deserialize<List<ExtractedCandidatePayload>>(ExtractJsonArray(content)) ?? [];

        var results = new List<ExtractedCandidate>();
        foreach (var entry in parsed)
        {
            if (string.IsNullOrWhiteSpace(entry.Content) || !Enum.TryParse<MemoryCategory>(entry.Category, ignoreCase: true, out var category))
            {
                continue;
            }

            results.Add(new ExtractedCandidate(
                entry.Content.Trim(), category, entry.IsExplicit, entry.IsSensitive,
                (decimal)Math.Clamp(entry.Confidence, 0.0, 1.0)));
        }

        return results;
    }

    /// <summary>Some providers wrap the JSON in prose or a markdown code fence despite instructions — take the outermost [...] span defensively.</summary>
    private static string ExtractJsonArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private sealed record ExtractedCandidate(string Content, MemoryCategory Category, bool IsExplicit, bool IsSensitive, decimal Confidence);

    private sealed record ExtractedCandidatePayload(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("isExplicit")] bool IsExplicit,
        [property: JsonPropertyName("isSensitive")] bool IsSensitive,
        [property: JsonPropertyName("confidence")] double Confidence);
}

internal static partial class MemoryExtractionJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory extraction response was not valid JSON for chat {UserChatId} — treating as no candidates found")]
    public static partial void ExtractionParseFailed(ILogger logger, Guid userChatId, Exception exception);
}
