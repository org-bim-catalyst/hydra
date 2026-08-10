using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptAuditLogRepository(AskLucyDbContext dbContext) : IPromptAuditLogRepository
{
    public void Add(PromptAuditLog entry) => dbContext.PromptAuditLogs.Add(entry);
}
