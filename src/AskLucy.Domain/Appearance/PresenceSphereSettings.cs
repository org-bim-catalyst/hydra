using AskLucy.Domain.Common;

namespace AskLucy.Domain.Appearance;

/// <summary>
/// specs/080 — the workspace-wide look of the presence sphere: how big its dots are, how much of its card it
/// fills, and whether users may zoom it. There is exactly one row, keyed by <see cref="SingletonId"/>; with no
/// row at all, the defaults apply, which are the look the sphere had before it became adjustable.
/// </summary>
public sealed class PresenceSphereSettings : BaseEntity
{
    public static readonly Guid SingletonId = new("0f7a1c52-3b6e-4d18-9a07-8e5d0c080001");

    public const decimal MinDotSizeMultiplier = 0.25m;
    public const decimal MaxDotSizeMultiplier = 2.00m;
    public const decimal DefaultDotSizeMultiplier = 1.00m;

    public const int MinCardFillPercent = 40;
    public const int MaxCardFillPercent = 95;
    public const int DefaultCardFillPercent = 75;

    public const bool DefaultZoomEnabled = false;

    private PresenceSphereSettings()
    {
        // Required by EF Core materialization.
    }

    /// <summary>Multiplies the dots' size: 1.00 is the size they had before this was adjustable.</summary>
    public decimal DotSizeMultiplier { get; private set; }

    /// <summary>The sphere's diameter as a percentage of the card's height.</summary>
    public int CardFillPercent { get; private set; }

    /// <summary>When true, users can zoom the sphere between a quarter of its normal size and twice it.</summary>
    public bool ZoomEnabled { get; private set; }

    public static bool IsValidDotSizeMultiplier(decimal value) => value is >= MinDotSizeMultiplier and <= MaxDotSizeMultiplier;

    public static bool IsValidCardFillPercent(int value) => value is >= MinCardFillPercent and <= MaxCardFillPercent;

    /// <summary>The first save: a row holding the given values, with who saved it and when.</summary>
    public static PresenceSphereSettings Create(decimal dotSizeMultiplier, int cardFillPercent, bool zoomEnabled, string actor, DateTime utcNow)
    {
        var settings = new PresenceSphereSettings
        {
            Id = SingletonId,
            CreatedAtUtc = utcNow,
            CreatedBy = actor,
        };
        settings.Update(dotSizeMultiplier, cardFillPercent, zoomEnabled, actor, utcNow);
        return settings;
    }

    /// <summary>Replaces all three values. Throws <see cref="ArgumentOutOfRangeException"/> for a value outside its range, changing nothing.</summary>
    public void Update(decimal dotSizeMultiplier, int cardFillPercent, bool zoomEnabled, string actor, DateTime utcNow)
    {
        if (!IsValidDotSizeMultiplier(dotSizeMultiplier))
        {
            throw new ArgumentOutOfRangeException(nameof(dotSizeMultiplier), dotSizeMultiplier,
                $"The dot size must be between {MinDotSizeMultiplier} and {MaxDotSizeMultiplier}.");
        }

        if (!IsValidCardFillPercent(cardFillPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(cardFillPercent), cardFillPercent,
                $"The size within the card must be between {MinCardFillPercent} and {MaxCardFillPercent} percent.");
        }

        DotSizeMultiplier = dotSizeMultiplier;
        CardFillPercent = cardFillPercent;
        ZoomEnabled = zoomEnabled;
        ModifiedAtUtc = utcNow;
        ModifiedBy = actor;
    }
}
