using FluentValidation;

namespace AskLucy.Application.Users.Commands.ForceReset2fa;

public sealed class ForceReset2faCommandValidator : AbstractValidator<ForceReset2faCommand>
{
    public ForceReset2faCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
