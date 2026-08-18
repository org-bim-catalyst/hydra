using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptTestCaseRepository(AskLucyDbContext dbContext) : IPromptTestCaseRepository
{
    public Task<PromptTestCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PromptTestCases.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PromptTestCase>> ListForPromptAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        await dbContext.PromptTestCases
            .Where(t => t.PromptId == promptId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(PromptTestCase testCase) => dbContext.PromptTestCases.Add(testCase);
}
