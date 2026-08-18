using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using MediatR;

namespace AskLucy.Application.Memory.Commands.EditMemory;

/// <summary>
/// spec.md FR-019, User Story 2 AC2. Unlike <c>MemoryConflictDetectionService</c>'s
/// direct-contradiction merge (a documented known limitation — see its doc comment), a deliberate
/// user edit here does re-embed the new content: the user chose to correct/refine what Lucy
/// remembers, so future retrieval must rank it by the content that's actually there now, not a
/// stale pre-edit vector.
/// </summary>
public sealed class EditMemoryCommandHandler(
    IMemoryRepository memoryRepository,
    IMemoryVersionRepository versionRepository,
    IMemoryAuditLogRepository auditLogRepository,
    IMemoryEmbeddingRepository embeddingRepository,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IMemoryVectorStore vectorStore,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<EditMemoryCommand>
{
    public async Task Handle(EditMemoryCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        var previousContent = memory.Edit(request.Content, userId);
        versionRepository.Add(MemoryVersion.Create(memory.Id, previousContent, MemoryChangeReason.UserEdit, userId));
        auditLogRepository.Add(MemoryAuditLog.Create(memory.Id, memory.UserId, userId, MemoryAuditAction.Edited, null, userId));

        var currentEmbedding = await embeddingRepository.GetCurrentByMemoryIdAsync(memory.Id, cancellationToken);
        currentEmbedding?.MarkSuperseded(userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var provider = await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken)
            ?? throw new InvalidOperationException("No default embedding provider is configured.");
        var embeddingService = embeddingServiceResolver.Resolve(provider.Vendor);
        var embeddingResult = await embeddingService.EmbedAsync(memory.Content, cancellationToken);

        var newEmbedding = MemoryEmbedding.Create(memory.Id, provider.Id, embeddingResult.Vector, userId);
        embeddingRepository.Add(newEmbedding);
        await unitOfWork.SaveChangesAsync(cancellationToken); // The row must exist before the raw-SQL vector UPDATE below.

        await vectorStore.UpsertAsync(memory.Id, newEmbedding.Id, embeddingResult.Vector, cancellationToken);
    }
}
