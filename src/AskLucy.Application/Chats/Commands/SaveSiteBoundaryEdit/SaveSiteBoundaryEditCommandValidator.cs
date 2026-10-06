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

    /// <summary>specs/081 - voids (atriums, courtyards) one ring may have.</summary>
    public const int MaxVoidsPerRing = 50;

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

        RuleFor(c => c).Custom((command, context) =>
        {
            if (VoidsProblem(command.Rings, command.Voids) is { } problem)
            {
                context.AddFailure(nameof(command.Voids), problem);
            }
        });
    }

    /// <summary>
    /// specs/081 - the bounds on a request's voids, shared with the combine request: no voids for rings it
    /// does not have, at most <see cref="MaxVoidsPerRing"/> a ring, 3 to <see cref="MaxCornersPerRing"/> corners
    /// each, real coordinates, and every corner of rings and voids together within <see cref="MaxCornersTotal"/>.
    /// Null when they are fine.
    /// </summary>
    public static string? VoidsProblem(
        IReadOnlyList<IReadOnlyList<GeoPoint>>? rings, IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>? voids)
    {
        if (voids is null || voids.Count == 0)
        {
            return null;
        }

        if (rings is null || voids.Count > rings.Count)
        {
            return "There are voids for more rings than the outline has.";
        }

        if (voids.Any(ringVoids => ringVoids is null || ringVoids.Count > MaxVoidsPerRing))
        {
            return $"A ring has at most {MaxVoidsPerRing} voids.";
        }

        var every = voids.SelectMany(ringVoids => ringVoids).ToList();
        if (every.Any(v => v is null || CornerCount(v) < MinCornersPerRing || CornerCount(v) > MaxCornersPerRing))
        {
            return $"Every void needs {MinCornersPerRing} to {MaxCornersPerRing} corners.";
        }

        if (every.Any(v => v.Any(p => !double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude) ||
                p.Latitude is < -90 or > 90 || p.Longitude is < -180 or > 180)))
        {
            return "Every void corner needs a valid latitude and longitude.";
        }

        return rings.Sum(r => r?.Count ?? 0) + every.Sum(v => v.Count) > MaxCornersTotal
            ? $"An outline has at most {MaxCornersTotal} corners in total, voids included."
            : null;
    }

    /// <summary>Corners without a repeated closing corner, which the API's closed rings carry.</summary>
    private static int CornerCount(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count;
}
