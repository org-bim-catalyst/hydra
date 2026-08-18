using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="PromptCategory"/> — mirrors <c>KnowledgeBaseCategory</c>'s predefined-vs-custom shape (research.md Decision 6).</summary>
public interface IPromptCategoryRepository
{
    Task<PromptCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Predefined (owner-null) rows plus the caller's own custom rows.</summary>
    Task<IReadOnlyList<PromptCategory>> ListPredefinedAndCustomForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    Task<PromptCategory?> GetCustomByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default);

    void Add(PromptCategory category);
}
