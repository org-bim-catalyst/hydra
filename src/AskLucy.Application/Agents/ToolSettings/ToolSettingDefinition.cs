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
        bool defaultValue) =>
        new(toolName, key, ToolSettingValueType.Boolean, label, description, defaultValue ? "true" : "false");

    public static ToolSettingDefinition Choice(
        string toolName,
        string key,
        string label,
        string description,
        string defaultValue,
        IReadOnlyList<ToolSettingOption> options) =>
        new(toolName, key, ToolSettingValueType.Choice, label, description, defaultValue, options);

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
}

/// <summary>One permitted value of a choice setting, with the label shown for it.</summary>
/// <param name="Value">The stored value.</param>
/// <param name="Label">What the administrator sees.</param>
public sealed record ToolSettingOption(string Value, string Label);
