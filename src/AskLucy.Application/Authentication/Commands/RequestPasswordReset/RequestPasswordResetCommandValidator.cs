using FluentValidation;

namespace AskLucy.Application.Authentication.Commands.RequestPasswordReset;

/// <summary>
/// Shape only. There is deliberately no rule that touches the account store: a validation failure
/// is visible to the caller, so any existence check here would be the enumeration oracle FR-003
/// exists to close.
/// </summary>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
