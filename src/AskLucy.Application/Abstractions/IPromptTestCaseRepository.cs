using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="PromptTestCase"/> (spec.md FR-043).</summary>
public interface IPromptTestCaseRepository
{
    Task<PromptTestCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromptTestCase>> ListForPromptAsync(Guid promptId, CancellationToken cancellationToken = default);

    void Add(PromptTestCase testCase);
}
