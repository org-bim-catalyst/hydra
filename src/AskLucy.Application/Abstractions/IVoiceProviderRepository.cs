using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="VoiceProvider"/> (specs/070, constitution &#167;3 Repository rules).</summary>
public interface IVoiceProviderRepository
{
    Task<VoiceProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<VoiceProvider?> GetByKeyAsync(string providerKey, CancellationToken cancellationToken = default);

    /// <summary>Every configured voice provider, ordered by <see cref="VoiceProvider.Priority"/> — the first row is Lucy's voice.</summary>
    Task<IReadOnlyList<VoiceProvider>> ListByPriorityAsync(CancellationToken cancellationToken = default);

    void Add(VoiceProvider provider);
}
