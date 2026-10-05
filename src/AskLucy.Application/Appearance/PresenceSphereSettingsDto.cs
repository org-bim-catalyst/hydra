using AskLucy.Application.Abstractions;
using AskLucy.Domain.Appearance;

namespace AskLucy.Application.Appearance;

/// <summary>
/// specs/080 contracts/presence-sphere-api.md. <see cref="IsDefault"/> is true while nobody has saved any
/// settings. <see cref="ModifiedBy"/> is a display name, never an e-mail address.
/// </summary>
public sealed record PresenceSphereSettingsDto(
    decimal DotSizeMultiplier,
    int CardFillPercent,
    bool ZoomEnabled,
    string? ModifiedBy,
    DateTime? ModifiedAtUtc,
    bool IsDefault)
{
    public static PresenceSphereSettingsDto Defaults { get; } = new(
        PresenceSphereSettings.DefaultDotSizeMultiplier,
        PresenceSphereSettings.DefaultCardFillPercent,
        PresenceSphereSettings.DefaultZoomEnabled,
        ModifiedBy: null,
        ModifiedAtUtc: null,
        IsDefault: true);

    /// <summary>The DTO for a stored row, naming the administrator who last saved it when they can still be found.</summary>
    public static async Task<PresenceSphereSettingsDto> FromAsync(
        PresenceSphereSettings settings,
        IUserProfileRepository profiles,
        CancellationToken cancellationToken)
    {
        string? name = null;
        if (settings.ModifiedBy is { Length: > 0 } actor)
        {
            var profile = await profiles.GetByIdAsync(actor, cancellationToken);
            var full = profile is null ? string.Empty : $"{profile.FirstName} {profile.LastName}".Trim();
            name = full.Length > 0 ? full : null;
        }

        return new PresenceSphereSettingsDto(
            settings.DotSizeMultiplier,
            settings.CardFillPercent,
            settings.ZoomEnabled,
            name,
            settings.ModifiedAtUtc,
            IsDefault: false);
    }
}
