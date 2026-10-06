using AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;
using AskLucy.Domain.SiteBoundaries;
using FluentValidation;

namespace AskLucy.Application.Chats.Commands.CombineSiteBoundaryShape;

/// <summary>The same ring bounds as saving an edit, plus a sane circle: 1 m to 5 km, somewhere on Earth.</summary>
public sealed class CombineSiteBoundaryShapeCommandValidator : AbstractValidator<CombineSiteBoundaryShapeCommand>
{
    public const double MinRadiusMeters = 1;
    public const double MaxRadiusMeters = 5_000;

    public CombineSiteBoundaryShapeCommandValidator()
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.Operation).IsInEnum();

        // specs/081: a circle (centre and radius) or a drawn polygon, never both and never neither.
        RuleFor(c => c)
            .Must(c => (c.Shape is null) != (c.Centre is null))
            .WithMessage("Send either a circle (centre and radius) or a shape, not both and not neither.")
            .OverridePropertyName("Shape");

        RuleFor(c => c.RadiusMeters)
            .Must(radius => double.IsFinite(radius) && radius is >= MinRadiusMeters and <= MaxRadiusMeters)
            .WithMessage($"The circle's radius must be between {MinRadiusMeters} m and {MaxRadiusMeters:N0} m.")
            .When(c => c.Shape is null);

        RuleFor(c => c.Centre)
            .Must(centre => centre is not null && IsOnEarth(centre))
            .WithMessage("The circle's centre needs a valid latitude and longitude.")
            .When(c => c.Shape is null);

        RuleFor(c => c.Shape)
            .Must(shape => shape!.Count is >= SaveSiteBoundaryEditCommandValidator.MinCornersPerRing and <= SaveSiteBoundaryEditCommandValidator.MaxCornersPerRing + 1)
            .WithMessage($"A shape needs {SaveSiteBoundaryEditCommandValidator.MinCornersPerRing} to {SaveSiteBoundaryEditCommandValidator.MaxCornersPerRing} corners.")
            .Must(shape => shape!.All(IsOnEarth))
            .WithMessage("Every corner of the shape needs a valid latitude and longitude.")
            .When(c => c.Shape is not null);

        RuleFor(c => c).Custom((command, context) =>
        {
            if (SaveSiteBoundaryEditCommandValidator.VoidsProblem(command.Rings, command.Voids) is { } problem)
            {
                context.AddFailure(nameof(command.Voids), problem);
            }
        });

        RuleFor(c => c.Rings)
            .Must(rings => rings is { Count: >= 1 and <= SaveSiteBoundaryEditCommandValidator.MaxRings })
            .WithMessage($"An outline has 1 to {SaveSiteBoundaryEditCommandValidator.MaxRings} rings.");

        RuleFor(c => c.Rings)
            .Must(rings => rings.All(ring => ring is not null &&
                ring.Count is >= SaveSiteBoundaryEditCommandValidator.MinCornersPerRing and <= SaveSiteBoundaryEditCommandValidator.MaxCornersPerRing + 1))
            .WithMessage($"Every ring needs {SaveSiteBoundaryEditCommandValidator.MinCornersPerRing} to {SaveSiteBoundaryEditCommandValidator.MaxCornersPerRing} corners.")
            .When(c => c.Rings is not null);

        RuleFor(c => c.Rings)
            .Must(rings => rings.Sum(ring => ring?.Count ?? 0) <= SaveSiteBoundaryEditCommandValidator.MaxCornersTotal)
            .WithMessage($"An outline has at most {SaveSiteBoundaryEditCommandValidator.MaxCornersTotal} corners in total.")
            .When(c => c.Rings is not null);

        RuleFor(c => c.Rings)
            .Must(rings => rings.All(ring => ring is not null && ring.All(IsOnEarth)))
            .WithMessage("Every corner needs a valid latitude and longitude.")
            .When(c => c.Rings is not null);
    }

    private static bool IsOnEarth(GeoPoint p) =>
        double.IsFinite(p.Latitude) && double.IsFinite(p.Longitude) && p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180;
}
