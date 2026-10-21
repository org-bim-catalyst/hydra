using FluentValidation;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.MapCaptures.Queries.CaptureMap;

public sealed class CaptureMapQueryValidator : AbstractValidator<CaptureMapQuery>
{
    public CaptureMapQueryValidator(IOptions<MapCaptureOptions> options)
    {
        var bounds = options.Value;

        RuleFor(x => x.Location)
            .Must(location => !string.IsNullOrWhiteSpace(location))
            .When(x => x.Latitude is null && x.Longitude is null)
            .WithMessage("Provide either 'location' or both 'latitude' and 'longitude'.");

        RuleFor(x => x.Latitude)
            .NotNull()
            .When(x => x.Longitude is not null)
            .WithMessage("'latitude' and 'longitude' must be supplied together.");
        RuleFor(x => x.Longitude)
            .NotNull()
            .When(x => x.Latitude is not null)
            .WithMessage("'latitude' and 'longitude' must be supplied together.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitude is not null);

        RuleFor(x => x.ViewSizeMetres)
            .InclusiveBetween(bounds.MinViewSizeMetres, bounds.MaxViewSizeMetres)
            .When(x => x.ViewSizeMetres is not null);

        RuleFor(x => x.Mode)
            .Must(mode => MapCaptureWireFormat.TryParseMode(mode, out _))
            .When(x => x.Mode is not null)
            .WithMessage("Mode must be one of: roadmap, satellite, hybrid, buildings-only.");

        // site-boundary passes here on purpose: the handler rejects it as OverlayUnavailable so
        // the message can name the unsupported value.
        RuleFor(x => x.Overlays)
            .Must(overlays => MapCaptureWireFormat.TryParseOverlays(overlays, out _, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Overlays))
            .WithMessage("Overlays must be a comma-separated subset of: building-footprints, site-boundary.");
    }
}
