using FluentValidation;

namespace AskLucy.Application.Mcp.Commands.RegisterMcpServer;

public sealed class RegisterMcpServerCommandValidator : AbstractValidator<RegisterMcpServerCommand>
{
    public RegisterMcpServerCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(1000);
        RuleFor(c => c.Endpoint).NotEmpty().MaximumLength(400);
        RuleFor(c => c.CapabilityRefreshIntervalMinutes).InclusiveBetween(1, 1440);
        RuleFor(c => c.InsecureTransportJustification).NotEmpty().When(c => c.AllowInsecureTransport)
            .WithMessage("A justification is required when allowing an insecure transport (FR-049).");
        RuleFor(c => c.EndpointValidationJustification).NotEmpty().When(c => c.EndpointValidationOverride)
            .WithMessage("A justification is required when overriding endpoint validation (FR-050).");
    }
}
