using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryApprovalRepository
{
    Task<MemoryApproval?> GetByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default);

    void Add(MemoryApproval approval);
}
