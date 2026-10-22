namespace AskLucy.Application.Agents.ToolSettings;

/// <summary>
/// One setting a tool exposes to an administrator on the admin AI Tools page.
/// </summary>
/// <param name="ToolName">The tool the setting belongs to — its <c>IAgentTool.Name</c>.</param>
/// <param name="Key">The setting's own name, unique within the tool.</param>
/// <param name="ValueType">How the value is stored and edited.</param>
/// <param name="Label">Shown beside the control.</param>
/// <param name="Description">One or two sentences under it: what changes when the value is different.</param>
/// <param name="DefaultValue">What applies until an administrator saves a value.</param>
/// <param name="Options">
/// The permitted values of a <see cref="ToolSettingValueType.Choice"/> setting, in the order they
/// should be offered. Empty for every other type.
/// </param>
public sealed record ToolSettingDefinition(
    string ToolName,
    string Key,
    ToolSettingValueType ValueType,
    string Label,
    string Description,
    string DefaultValue,
    IReadOnlyList<ToolSettingOption>? Options = null)
{
    /// <summary>The permitted values of a choice setting — empty for every other type.</summary>
    public IReadOnlyList<ToolSettingOption> Choices => Options ?? [];

    public static ToolSettingDefinition Boolean(
        string toolName,
        string key,
        string label,
        string description,
        bool defaultValue,
        string? scopedBy = null,
        string? scopeValue = null) =>
        new(toolName, key, ToolSettingValueType.Boolean, label, description, defaultValue ? "true" : "false")
        {
            ScopedBy = scopedBy,
            ScopeValue = scopeValue,
        };

    public static ToolSettingDefinition Choice(
        string toolName,
        string key,
        string label,
        string description,
        string defaultValue,
        IReadOnlyList<ToolSettingOption> options,
        string? scopedBy = null) =>
        new(toolName, key, ToolSettingValueType.Choice, label, description, defaultValue, options)
        {
            ScopedBy = scopedBy,
        };

    public static ToolSettingDefinition WholeNumber(
        string toolName,
        string key,
        string label,
        string description,
        int defaultValue,
        int minimum,
        int maximum) =>
        new(toolName, key, ToolSettingValueType.Integer, label, description, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Minimum = minimum,
            Maximum = maximum,
        };

    /// <summary>The inclusive bounds of an <see cref="ToolSettingValueType.Integer"/> setting.</summary>
    public int? Minimum { get; init; }

    /// <summary>The inclusive bounds of an <see cref="ToolSettingValueType.Integer"/> setting.</summary>
    public int? Maximum { get; init; }

    /// <summary>
    /// The key of the setting whose value narrows this one's options — e.g. the zoom levels
    /// depend on the provider that will serve the capture. Null when every option always applies.
    /// </summary>
    public string? ScopedBy { get; init; }

    /// <summary>
    /// The value of the scoping setting this setting applies to, or null when it always applies.
    /// The colour correction, for instance, is Google's alone: it is not shown at all while Esri is
    /// the provider, because there is nothing there for it to correct.
    /// </summary>
    public string? ScopeValue { get; init; }
}

/// <summary>One permitted value of a choice setting, with the label shown for it.</summary>
/// <param name="Value">The stored value.</param>
/// <param name="Label">What the administrator sees.</param>
/// <param name="Scope">
/// The value of the scoping setting this option belongs to, or null when it always applies. A
/// zoom level, for instance, belongs to one provider: the levels Esri can assemble are not the
/// ones Google can, and offering the other provider's levels would be offering a request that
/// cannot be served.
/// </param>
public sealed record ToolSettingOption(string Value, string Label, string? Scope = null);
