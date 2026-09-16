using AskLucy.Application.Common;
using FluentValidation;

namespace AskLucy.Application.Users.Commands.BulkDeleteUsers;

public sealed class BulkDeleteUsersCommandValidator : AbstractValidator<BulkDeleteUsersCommand>
{
    public BulkDeleteUsersCommandValidator()
    {
        RuleFor(c => c.Target).SetValidator(new BulkTargetValidator());
    }
}
