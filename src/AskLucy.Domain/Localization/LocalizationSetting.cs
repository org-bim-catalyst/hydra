using System.Text.Json;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Localization;

/// <summary>
/// The platform's localization switch (FR-044a, research R14): a singleton row. While <see cref="IsEnabled"/> is false the whole
/// platform behaves as English-only, whatever users have chosen. <c>en</c> is always supported.
/// </summary>
public sealed class LocalizationSetting : BaseEntity
{
    /// <summary>The fixed id of the one row; the migration seeds it disabled with English only.</summary>
    public static readonly Guid SingletonId = new("0c6f4c2a-5b1e-4d3a-9a57-7a1f0b6c3e10");

    public const int SupportedLanguagesMaxLength = 200;

    public bool IsEnabled { get; private set; }

    public string SupportedLanguagesJson { get; private set; } = "[\"en\"]";

    public IReadOnlyList<string> SupportedLanguages =>
        JsonSerializer.Deserialize<string[]>(SupportedLanguagesJson) ?? [PlatformLanguages.English];

    private LocalizationSetting()
    {
        // Required by EF Core materialization.
    }

    /// <summary>The state a fresh installation starts in: disabled, English only.</summary>
    public static LocalizationSetting CreateDefault(DateTime now) => new()
    {
        Id = SingletonId,
        IsEnabled = false,
        SupportedLanguagesJson = "[\"en\"]",
        CreatedAtUtc = now,
        CreatedBy = "system",
    };

    /// <summary>
    /// Replaces the state. <c>en</c> is required and every code must be one the platform ships content for; the codes are stored in
    /// the platform's own spelling, de-duplicated, with English first.
    /// </summary>
    public void Update(bool isEnabled, IEnumerable<string> supportedLanguages)
    {
        ArgumentNullException.ThrowIfNull(supportedLanguages);

        var normalized = new List<string>();
        foreach (var code in supportedLanguages)
        {
            var platform = PlatformLanguages.Normalize(code)
                ?? throw new DomainRuleViolationException($"'{code}' is not a language the platform provides.");
            if (!normalized.Contains(platform, StringComparer.Ordinal))
            {
                normalized.Add(platform);
            }
        }

        if (!normalized.Contains(PlatformLanguages.English, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException("English is always supported and can't be removed.");
        }

        // Stable order: the platform's own, so English leads.
        var ordered = PlatformLanguages.All.Select(l => l.Code).Where(normalized.Contains).ToArray();
        IsEnabled = isEnabled;
        SupportedLanguagesJson = JsonSerializer.Serialize(ordered);
    }

    /// <summary>Whether <paramref name="code"/> is supported right now: localization is on and the code is listed. English is always supported.</summary>
    public bool Supports(string? code)
    {
        var normalized = PlatformLanguages.Normalize(code);
        if (normalized is null)
        {
            return false;
        }

        return normalized == PlatformLanguages.English || (IsEnabled && SupportedLanguages.Contains(normalized, StringComparer.Ordinal));
    }
}
