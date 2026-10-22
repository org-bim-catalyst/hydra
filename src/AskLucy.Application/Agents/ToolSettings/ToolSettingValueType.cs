namespace AskLucy.Application.Agents.ToolSettings;

// CA1720: not renamed — "Integer" is the wire value the admin page and the API contract use for
// this setting type, and the member name IS the serialized form (the API serializes every enum as
// a string). Renaming it would change the contract for no benefit.
#pragma warning disable CA1720
/// <summary>
/// The value types a tool setting can have. The value is stored as text either way, so a new type
/// is a catalogue change, never a migration.
/// </summary>
public enum ToolSettingValueType
{
    /// <summary>An on/off switch — the invariant form is <c>"true"</c>/<c>"false"</c>.</summary>
    Boolean,

    /// <summary>One of a fixed set of values, each with its own label.</summary>
    Choice,

    /// <summary>A whole number, parsed from its invariant text form.</summary>
    Integer,
}
#pragma warning restore CA1720
