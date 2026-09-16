using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Users.Commands.BulkUnlockUsers;

public sealed class BulkUnlockUsersCommandValidator : AbstractValidator<BulkUnlockUsersCommand>
{
    public BulkUnlockUsersCommandValidator()
    {
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
