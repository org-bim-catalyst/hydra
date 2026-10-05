using AskLucy.Domain.Appearance;

namespace AskLucy.Application.Abstractions;

/// <summary>specs/080 — the single workspace-wide <see cref="PresenceSphereSettings"/> row.</summary>
public interface IPresenceSphereSettingsRepository
{
    /// <summary>The tracked row, or null when nobody has saved any settings yet (the defaults then apply). Never creates it.</summary>
    Task<PresenceSphereSettings?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds the first row; it is committed through the request's <see cref="IUnitOfWork"/>.</summary>
    void Add(PresenceSphereSettings settings);
}
