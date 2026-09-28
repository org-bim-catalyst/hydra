using AskLucy.Domain.Ai.Dictation;

namespace AskLucy.Application.Abstractions;

/// <summary>specs/078 — the single platform-wide <see cref="DictationEngineSetting"/> row.</summary>
public interface IDictationEngineSettingRepository
{
    /// <summary>
    /// The tracked setting. The first call on a fresh database inserts the default row on its own,
    /// without flushing anything else the request is tracking; a concurrent first insert is
    /// resolved by re-reading. Changes are committed through the request's <see cref="IUnitOfWork"/>.
    /// </summary>
    Task<DictationEngineSetting> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>The selected Local Whisper model's <c>CustomModels.Id</c>, or null. A read only: never creates the row.</summary>
    Task<Guid?> GetSelectedLocalWhisperModelIdAsync(CancellationToken cancellationToken = default);
}
