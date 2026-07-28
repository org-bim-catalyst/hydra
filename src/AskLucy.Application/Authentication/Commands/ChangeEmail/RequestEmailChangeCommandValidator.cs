using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

public sealed class RequestEmailChangeCommandValidator : AbstractValidator<RequestEmailChangeCommand>
{
    public RequestEmailChangeCommandValidator()
    {
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress();
    }
}
