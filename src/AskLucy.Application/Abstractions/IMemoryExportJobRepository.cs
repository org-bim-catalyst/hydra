using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="MemoryExportJob"/> (constitution §3 Repository rules).</summary>
public interface IMemoryExportJobRepository
{
    Task<MemoryExportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(MemoryExportJob job);
}
