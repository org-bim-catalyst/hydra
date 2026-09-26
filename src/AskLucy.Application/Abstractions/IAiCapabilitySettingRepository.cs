using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>specs/077 — stored values of the per-capability settings <see cref="Ai.CapabilitySettings.CapabilitySettingCatalog"/> declares.</summary>
public interface IAiCapabilitySettingRepository
{
    Task<IReadOnlyList<AiCapabilitySetting>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiCapabilitySetting>> ListByCapabilityAsync(AiCapability capability, CancellationToken cancellationToken = default);

    Task<AiCapabilitySetting?> GetAsync(AiCapability capability, string key, CancellationToken cancellationToken = default);

    void Add(AiCapabilitySetting setting);
}
