using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory.Authorization;
using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemory;

public sealed class GetMemoryQueryHandler(
    IMemoryRepository memoryRepository, IMemoryVersionRepository versionRepository,
    IMemoryConflictRepository conflictRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMemoryQuery, MemoryDetailDto>
{
    public async Task<MemoryDetailDto> Handle(GetMemoryQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var memory = MemoryOwnershipGuard.EnsureOwnedBy(await memoryRepository.GetByIdAsync(request.MemoryId, cancellationToken), userId);

        var history = await versionRepository.GetByMemoryIdAsync(memory.Id, cancellationToken);
        var openConflict = await conflictRepository.GetOpenByMemoryIdAsync(memory.Id, cancellationToken);

        return new MemoryDetailDto(
            memory.Id, memory.Category.ToString(), memory.Content, memory.State.ToString(), memory.IsSensitive,
            memory.ProjectId, memory.Importance, memory.Confidence,
            history.Select(v => new MemoryVersionDto(v.PreviousContent, v.ChangeReason.ToString(), v.ChangedAtUtc, v.ChangedByActor)).ToList(),
            openConflict is null
                ? null
                : new OpenConflictDto(openConflict.Id, openConflict.ConflictType.ToString(), openConflict.ExistingMemoryId, openConflict.NewMemoryId, openConflict.DetectedAtUtc));
    }
}
