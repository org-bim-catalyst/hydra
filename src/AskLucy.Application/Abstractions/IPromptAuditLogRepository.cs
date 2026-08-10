using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

/// <summary>Append-only repository for <see cref="PromptAuditLog"/> (spec.md FR-090).</summary>
public interface IPromptAuditLogRepository
{
    void Add(PromptAuditLog entry);
}
