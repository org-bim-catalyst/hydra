using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Persistence.Repositories;

public sealed class KnowledgeBaseAuditLogRepository(AskLucyDbContext dbContext) : IKnowledgeBaseAuditLogRepository
{
    public void Add(KnowledgeBaseAuditLog entry) => dbContext.KnowledgeBaseAuditLogs.Add(entry);
}
