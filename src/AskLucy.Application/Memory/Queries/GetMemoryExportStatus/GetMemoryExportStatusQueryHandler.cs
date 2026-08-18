using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryExportStatus;

/// <summary>Owner-scoped (FR-027 posture) — a job the caller didn't request reports not-found, never confirming another user's export exists.</summary>
public sealed class GetMemoryExportStatusQueryHandler(
    IMemoryExportJobRepository exportJobRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMemoryExportStatusQuery, MemoryExportStatusDto>
{
    public async Task<MemoryExportStatusDto> Handle(GetMemoryExportStatusQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var job = await exportJobRepository.GetByIdAsync(request.ExportJobId, cancellationToken);

        if (job is null || !job.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Export job not found.");
        }

        return new MemoryExportStatusDto(job.Status.ToString(), job.StoredFileName);
    }
}
