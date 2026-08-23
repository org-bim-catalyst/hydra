using FluentValidation;

namespace AskLucy.Application.SiteAnalysis.Commands.VerifyAndLinkDigitalCoreProject;

public sealed class VerifyAndLinkDigitalCoreProjectCommandValidator : AbstractValidator<VerifyAndLinkDigitalCoreProjectCommand>
{
    public VerifyAndLinkDigitalCoreProjectCommandValidator()
    {
        RuleFor(c => c.UserChatId).NotEmpty();
        RuleFor(c => c.SiteName).NotEmpty().MaximumLength(300);
        RuleFor(c => c.Latitude).InclusiveBetween(-90, 90).When(c => c.Latitude is not null);
        RuleFor(c => c.Longitude).InclusiveBetween(-180, 180).When(c => c.Longitude is not null);
    }
}
