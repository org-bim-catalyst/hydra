using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryVersionRepository
{
    Task<IReadOnlyList<MemoryVersion>> GetByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default);

    void Add(MemoryVersion version);
}
