using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryExportJobRepository(AskLucyDbContext dbContext) : IMemoryExportJobRepository
{
    public Task<MemoryExportJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemoryExportJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public void Add(MemoryExportJob job) => dbContext.MemoryExportJobs.Add(job);
}
