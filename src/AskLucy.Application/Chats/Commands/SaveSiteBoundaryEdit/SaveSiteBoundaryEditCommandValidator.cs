using AskLucy.Domain.SiteBoundaries;
using FluentValidation;

namespace AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;

/// <summary>Bounds from contracts/site-boundary-edit-api.md: 1-20 rings, 3-2,000 corners each, 5,000 in total.</summary>
public sealed class SaveSiteBoundaryEditCommandValidator : AbstractValidator<SaveSiteBoundaryEditCommand>
{
    public const int MaxRings = 20;
    public const int MinCornersPerRing = 3;
    public const int MaxCornersPerRing = 2_000;
    public const int MaxCornersTotal = 5_000;

    public SaveSiteBoundaryEditCommandValidator()
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.ExpectedRevision)
            .Must(revision => Guid.TryParse(revision, out _))
            .WithMessage("The revision must be a valid identifier.");

        RuleFor(c => c.Rings)
            .Must(rings => rings is { Count: >= 1 and <= MaxRings })
            .WithMessage($"An outline has 1 to {MaxRings} rings.");

        RuleFor(c => c.Rings)
            .Must(rings => rings.All(ring => ring is not null && CornerCount(ring) >= MinCornersPerRing && CornerCount(ring) <= MaxCornersPerRing))
            .WithMessage($"Every ring needs {MinCornersPerRing} to {MaxCornersPerRing} corners.")
            .When(c => c.Rings is not null);

        RuleFor(c => c.Rings)
            .Must(rings => rings.Sum(ring => ring?.Count ?? 0) <= MaxCornersTotal)
            .WithMessage($"An outline has at most {MaxCornersTotal} corners in total.")
            .When(c => c.Rings is not null);

        RuleFor(c => c.Rings)
            .Must(rings => rings.All(ring => ring is not null && ring.All(p =>
                double.IsFinite(p.Latitude) && double.IsFinite(p.Longitude) &&
                p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180)))
            .WithMessage("Every corner needs a valid latitude and longitude.")
            .When(c => c.Rings is not null);
    }

    /// <summary>Corners without a repeated closing corner, which the API's closed rings carry.</summary>
    private static int CornerCount(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count;
}
