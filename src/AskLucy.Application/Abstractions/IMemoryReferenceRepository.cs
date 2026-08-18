using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

public interface IMemoryReferenceRepository
{
    Task<IReadOnlyList<MemoryReference>> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);

    void Add(MemoryReference reference);

    void AddRange(IEnumerable<MemoryReference> references);
}
