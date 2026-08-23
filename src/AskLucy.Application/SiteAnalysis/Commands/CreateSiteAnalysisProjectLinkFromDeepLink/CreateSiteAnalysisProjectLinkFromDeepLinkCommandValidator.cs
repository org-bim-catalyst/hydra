using FluentValidation;

namespace AskLucy.Application.SiteAnalysis.Commands.CreateSiteAnalysisProjectLinkFromDeepLink;

public sealed class CreateSiteAnalysisProjectLinkFromDeepLinkCommandValidator
    : AbstractValidator<CreateSiteAnalysisProjectLinkFromDeepLinkCommand>
{
    public CreateSiteAnalysisProjectLinkFromDeepLinkCommandValidator()
    {
        RuleFor(c => c.TheDigitalCoreProjectId).NotEmpty().MaximumLength(200);
        RuleFor(c => c.SiteName).NotEmpty().MaximumLength(300);
    }
}
