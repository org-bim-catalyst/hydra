using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.CapabilitySettings;

/// <summary>specs/077 — the value types a capability setting can have. Only on/off switches exist so far.</summary>
public enum CapabilitySettingValueType
{
    Boolean,
}

/// <summary>
/// specs/077 — one setting an <see cref="AiCapability"/> exposes on the admin AI Capabilities page.
/// </summary>
/// <param name="Label">Shown beside the control.</param>
/// <param name="Description">One or two sentences under it: what changes when it is switched.</param>
/// <param name="DefaultValue">What applies until an administrator saves a value — the invariant form (<c>"true"</c>/<c>"false"</c>).</param>
public sealed record CapabilitySettingDefinition(
    AiCapability Capability,
    string Key,
    CapabilitySettingValueType ValueType,
    string Label,
    string Description,
    string DefaultValue);

/// <summary>
/// specs/077 — every setting any capability has. A capability with none here shows a disabled
/// gear on the admin page; adding one here is all it takes to enable it (values are stored as
/// key/value rows, so no migration either).
/// </summary>
public sealed class CapabilitySettingCatalog
{
    /// <summary>Whether a resolved site boundary grows to include the buildings connected to it that carry its name.</summary>
    public const string IncludeConnectedBuildingsKey = "includeConnectedBuildings";

    public IReadOnlyList<CapabilitySettingDefinition> Definitions { get; } =
    [
        new(
            AiCapability.BoundaryVision,
            IncludeConnectedBuildingsKey,
            CapabilitySettingValueType.Boolean,
            "Include connected buildings of the same development",
            "When on, a site's highlight also takes in buildings that share a wall with it and carry its name " +
            "(BurJuman's office tower and hotel), lists nearby namesakes such as a metro station, and Lucy asks which " +
            "to keep. When off, only the site itself is highlighted.",
            "true"),
    ];

    public IReadOnlyList<CapabilitySettingDefinition> For(AiCapability capability) =>
        [.. Definitions.Where(d => d.Capability == capability)];

    public CapabilitySettingDefinition? Find(AiCapability capability, string key) =>
        Definitions.FirstOrDefault(d => d.Capability == capability && string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="value"/> is a valid value for <paramref name="definition"/>, in its invariant form.</summary>
    public static bool IsValidValue(CapabilitySettingDefinition definition, string? value) => definition.ValueType switch
    {
        CapabilitySettingValueType.Boolean => value is "true" or "false",
        _ => false,
    };
}
