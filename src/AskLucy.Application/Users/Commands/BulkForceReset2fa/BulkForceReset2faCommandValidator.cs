using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Users.Commands.BulkForceReset2fa;

public sealed class BulkForceReset2faCommandValidator : AbstractValidator<BulkForceReset2faCommand>
{
    public BulkForceReset2faCommandValidator()
    {
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
