using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.RequestAccountSupport;

public sealed class RequestAccountSupportCommandValidator : AbstractValidator<RequestAccountSupportCommand>
{
    public RequestAccountSupportCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress();

        // Bounded so the relayed body cannot be used to mail arbitrary volumes of text to the
        // support mailbox from an anonymous endpoint.
        RuleFor(c => c.Message).NotEmpty().MaximumLength(2000);
    }
}
