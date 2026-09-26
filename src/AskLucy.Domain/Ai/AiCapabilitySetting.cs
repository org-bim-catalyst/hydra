using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/077 — one administrator-set value for one of an <see cref="AiCapability"/>'s own
/// settings, e.g. whether boundary resolution includes the buildings connected to a site. At most
/// one row per capability and key; a setting with no row takes the default its definition
/// declares, so a capability never needs a row to work.
/// <para>
/// The value is stored as text rather than one column per type: which settings exist, and what
/// type each has, is declared by the Application layer's catalog, and adding a setting must not
/// need a migration.
/// </para>
/// </summary>
public sealed class AiCapabilitySetting : BaseEntity
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 500;

    private AiCapabilitySetting() { }

    public AiCapability Capability { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public static AiCapabilitySetting Create(AiCapability capability, string key, string value, string actor)
    {
        EnsureValid(key, value);

        return new AiCapabilitySetting
        {
            Id = Guid.CreateVersion7(),
            Capability = capability,
            Key = key,
            Value = value,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Change(string value, string actor)
    {
        EnsureValid(Key, value);

        Value = value;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    private static void EnsureValid(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            throw new DomainRuleViolationException($"A capability setting key must be 1 to {MaxKeyLength} characters.");
        }

        if (value is null || value.Length > MaxValueLength)
        {
            throw new DomainRuleViolationException($"A capability setting value must be at most {MaxValueLength} characters.");
        }
    }
}
