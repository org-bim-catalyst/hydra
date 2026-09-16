using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Users.Commands.BulkLockUsers;

public sealed class BulkLockUsersCommandValidator : AbstractValidator<BulkLockUsersCommand>
{
    public BulkLockUsersCommandValidator()
    {
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
