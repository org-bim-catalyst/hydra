using AskLucy.Application.Abstractions;

namespace AskLucy.Persistence;

public sealed class UnitOfWork(AskLucyDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
