using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.CapabilitySettings;

/// <summary>specs/077 — what a capability reads at run time: the saved value, or the catalog default when none is saved.</summary>
public interface ICapabilitySettingsReader
{
    Task<bool> GetBooleanAsync(AiCapability capability, string key, CancellationToken cancellationToken = default);
}

public sealed class CapabilitySettingsReader(
    IAiCapabilitySettingRepository settings, CapabilitySettingCatalog catalog) : ICapabilitySettingsReader
{
    public async Task<bool> GetBooleanAsync(AiCapability capability, string key, CancellationToken cancellationToken = default)
    {
        // An undeclared key is a programming error, not a missing value — failing loudly here is
        // what keeps a typo from reading as "off" forever.
        var definition = catalog.Find(capability, key)
            ?? throw new InvalidOperationException($"{capability} declares no setting '{key}'.");

        var stored = await settings.GetAsync(capability, key, cancellationToken);
        var value = stored is not null && CapabilitySettingCatalog.IsValidValue(definition, stored.Value)
            ? stored.Value
            : definition.DefaultValue;
        return value == "true";
    }
}
