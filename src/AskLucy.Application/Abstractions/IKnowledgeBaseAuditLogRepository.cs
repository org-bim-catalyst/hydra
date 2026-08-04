using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>Append-only log repository for <see cref="KnowledgeBaseAuditLog"/> (constitution §3 Repository rules, FR-011).</summary>
public interface IKnowledgeBaseAuditLogRepository
{
    void Add(KnowledgeBaseAuditLog entry);
}
