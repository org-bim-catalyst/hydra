using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using Microsoft.Extensions.Logging;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Memory;

/// <summary>
/// Implements <see cref="IMemoryConflictDetectionService"/> (research.md Decision 10). Lives in
/// <c>AskLucy.Application</c>, not <c>Infrastructure</c> — pure orchestration over Application
/// abstractions (<see cref="IMemoryVectorStore"/>, repositories, <see cref="IAIProvider"/>), no
/// framework-specific code, the same reasoning already recorded on <c>IMemoryExtractionJob</c>'s
/// doc comment and mirroring <c>IDocumentProcessingPipeline</c>/<c>DocumentProcessingPipeline</c>'s
/// established placement (a deviation from plan.md's originally-proposed Infrastructure path,
/// discovered during <c>/speckit-implement</c>).
///
/// <para>Does not call <see cref="IUnitOfWork.SaveChangesAsync"/> itself — per this interface's
/// contract, <paramref name="candidateMemory"/>[sic] arrives already <c>repository.Add</c>-ed but
/// not yet saved; this method's own repository mutations join that same not-yet-committed unit of
/// work, so the caller's single <c>SaveChangesAsync</c> call commits everything — candidate,
/// conflict row, version row, and audit logs — as one business transaction (constitution §3).
/// <see cref="IMemoryNotifier.NotifyAsync"/> is the one exception: it persists and pushes a
/// notification through its own internal transaction, mirroring <c>IProcessingNotifier</c>'s
/// established idiom.</para>
///
/// <para>Known scope boundary: a <see cref="ConflictVerdict.DirectContradiction"/> merge updates
/// <see cref="MemoryEntity.Content"/> but does not re-embed it — the existing
/// <see cref="AskLucy.Domain.Memory.MemoryEmbedding"/> row is left pointing at the pre-merge
/// vector until the next time that memory is otherwise re-embedded. Out of scope for this task
/// (tasks.md T030); noted here rather than silently accepted.</para>
/// </summary>
public sealed class MemoryConflictDetectionService(
    IMemoryVectorStore vectorStore,
    IMemoryRepository memoryRepository,
    IMemoryConflictRepository conflictRepository,
    IMemoryVersionRepository versionRepository,
    IMemoryAuditLogRepository auditLogRepository,
    IMemoryNotifier notifier,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IAIProviderRepository aiProviderRepository,
    IAIModelRepository aiModelRepository,
    IAIProviderResolver aiProviderResolver,
    AiCapabilityProviderResolver capabilityProviderResolver,
    ILogger<MemoryConflictDetectionService> logger) : IMemoryConflictDetectionService
{
    private const string SystemActor = "system:memory-conflict-detection";
    private const int CandidatePoolSize = 5;
    private const double SimilarityThreshold = 0.6;

    /// <summary>v1 — versioned per constitution §9 ("Prompt engineering... reviewed like code, testable in isolation").</summary>
    private const string ClassificationSystemPromptV1 =
        "You compare a new personal-memory statement against a small set of the same user's " +
        "existing remembered statements. For EACH existing statement, decide exactly one verdict: " +
        "\"NoConflict\" (unrelated or fully compatible), \"DirectContradiction\" (the new statement " +
        "plainly replaces/reverses the existing one — e.g. a changed preference or fact), or " +
        "\"AmbiguousSupersedeOrSupplement\" (they might conflict, or the new one might just add to " +
        "the old one, but it isn't clear-cut). Respond with ONLY a single JSON array (no markdown, " +
        "no commentary), one object per existing statement, each shaped exactly as " +
        "{\"memoryId\":\"<guid, verbatim from input>\",\"verdict\":\"<one of the three above>\"}.";

    public async Task<bool> DetectAndResolveAsync(MemoryEntity candidateMemory, CancellationToken cancellationToken = default)
    {
        var poolMemories = await FindCandidatePoolAsync(candidateMemory, cancellationToken);
        if (poolMemories.Count == 0)
        {
            return false;
        }

        var verdicts = await ClassifyAsync(candidateMemory, poolMemories, cancellationToken);

        MemoryEntity? directContradiction = null;
        var ambiguous = new List<MemoryEntity>();

        foreach (var (memory, verdict) in verdicts)
        {
            switch (verdict)
            {
                case ConflictVerdict.DirectContradiction when directContradiction is null:
                    directContradiction = memory;
                    break;
                case ConflictVerdict.AmbiguousSupersedeOrSupplement:
                    ambiguous.Add(memory);
                    break;
            }
        }

        if (directContradiction is not null)
        {
            ApplyDirectContradiction(candidateMemory, directContradiction);
            return true;
        }

        foreach (var memory in ambiguous)
        {
            ApplyAmbiguousConflict(candidateMemory, memory);
            await notifier.NotifyAsync(
                memory.UserId, candidateMemory.Id, MemoryNotificationEventType.ConflictNeedsConfirmation,
                "Lucy noticed something that might conflict with what she already remembers — please review it.",
                cancellationToken);
        }

        return false;
    }

    private async Task<IReadOnlyList<MemoryEntity>> FindCandidatePoolAsync(MemoryEntity candidateMemory, CancellationToken cancellationToken)
    {
        var provider = await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken)
            ?? throw new InvalidOperationException("No default embedding provider is configured.");
        var embeddingService = embeddingServiceResolver.Resolve(provider.Vendor);
        var candidateEmbedding = await embeddingService.EmbedAsync(candidateMemory.Content, cancellationToken);

        var nearest = await vectorStore.QueryNearestAsync(
            candidateEmbedding.Vector, candidateMemory.UserId, candidateMemory.ProjectId,
            CandidatePoolSize, SimilarityThreshold, cancellationToken);

        var poolMemoryIds = nearest.Select(c => c.MemoryId).Where(id => id != candidateMemory.Id).ToList();
        if (poolMemoryIds.Count == 0)
        {
            return [];
        }

        return await memoryRepository.GetByIdsAsync(poolMemoryIds, cancellationToken);
    }

    private void ApplyDirectContradiction(MemoryEntity candidateMemory, MemoryEntity existingMemory)
    {
        var previousContent = existingMemory.Edit(candidateMemory.Content, SystemActor);
        versionRepository.Add(MemoryVersion.Create(existingMemory.Id, previousContent, MemoryChangeReason.ConflictResolutionSupersede, SystemActor));

        candidateMemory.SoftDelete(SystemActor);

        var conflict = MemoryConflict.CreateAutoResolved(existingMemory.Id, SystemActor);
        conflictRepository.Add(conflict);

        auditLogRepository.Add(MemoryAuditLog.Create(
            existingMemory.Id, existingMemory.UserId, SystemActor, MemoryAuditAction.ConflictDetected, ConflictDetailsJson(conflict.Id), SystemActor));
        auditLogRepository.Add(MemoryAuditLog.Create(
            existingMemory.Id, existingMemory.UserId, SystemActor, MemoryAuditAction.ConflictResolved, ConflictDetailsJson(conflict.Id), SystemActor));
    }

    private void ApplyAmbiguousConflict(MemoryEntity candidateMemory, MemoryEntity existingMemory)
    {
        var conflict = MemoryConflict.CreatePendingConfirmation(existingMemory.Id, candidateMemory.Id, SystemActor);
        conflictRepository.Add(conflict);

        auditLogRepository.Add(MemoryAuditLog.Create(
            existingMemory.Id, existingMemory.UserId, SystemActor, MemoryAuditAction.ConflictDetected, ConflictDetailsJson(conflict.Id), SystemActor));
    }

    private static string ConflictDetailsJson(Guid conflictId) => JsonSerializer.Serialize(new { conflictId });

    private async Task<IReadOnlyList<(MemoryEntity Memory, ConflictVerdict Verdict)>> ClassifyAsync(
        MemoryEntity candidateMemory, IReadOnlyList<MemoryEntity> poolMemories, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.MemoryConflictDetection, cancellationToken);
            var provider = await aiProviderRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException("Provider not found.");
            var model = await aiModelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
                ?? throw new KeyNotFoundException("Model not found.");
            var aiProvider = aiProviderResolver.Resolve(provider.ProviderKey);

            var poolPayload = poolMemories.Select(m => new PoolMemoryPayload(m.Id.ToString(), m.Content));
            var poolJson = JsonSerializer.Serialize(poolPayload);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, ClassificationSystemPromptV1),
                new(ChatRole.User, $"New statement: \"{candidateMemory.Content}\"\n\nExisting statements (JSON array):\n{poolJson}"),
            };

            var completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters: null, cancellationToken);
            var verdictsByMemoryId = ParseVerdicts(completion.Content);

            return poolMemories
                .Select(m => (m, verdictsByMemoryId.GetValueOrDefault(m.Id, ConflictVerdict.NoConflict)))
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryConflictDetectionServiceLog.ClassificationFailed(logger, candidateMemory.Id, ex);
            return poolMemories.Select(m => (m, ConflictVerdict.NoConflict)).ToList();
        }
    }

    private static Dictionary<Guid, ConflictVerdict> ParseVerdicts(string content)
    {
        var result = new Dictionary<Guid, ConflictVerdict>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<VerdictPayload>>(ExtractJsonArray(content)) ?? [];

            foreach (var entry in parsed)
            {
                if (Guid.TryParse(entry.MemoryId, out var memoryId) && Enum.TryParse<ConflictVerdict>(entry.Verdict, ignoreCase: true, out var verdict))
                {
                    result[memoryId] = verdict;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed/unparseable model output — every pooled memory defaults to NoConflict via
            // the caller's GetValueOrDefault, matching this service's "never block memory creation
            // on an AI classification hiccup" posture.
        }

        return result;
    }

    /// <summary>Some providers wrap the JSON in prose or a markdown code fence despite instructions — take the outermost [...] span defensively.</summary>
    private static string ExtractJsonArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private enum ConflictVerdict
    {
        NoConflict,
        DirectContradiction,
        AmbiguousSupersedeOrSupplement,
    }

    private sealed record PoolMemoryPayload(
        [property: JsonPropertyName("memoryId")] string MemoryId,
        [property: JsonPropertyName("content")] string Content);

    private sealed record VerdictPayload(
        [property: JsonPropertyName("memoryId")] string MemoryId,
        [property: JsonPropertyName("verdict")] string Verdict);
}

internal static partial class MemoryConflictDetectionServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory conflict classification failed for candidate {CandidateMemoryId} — defaulting every pooled memory to NoConflict")]
    public static partial void ClassificationFailed(ILogger logger, Guid candidateMemoryId, Exception exception);
}
