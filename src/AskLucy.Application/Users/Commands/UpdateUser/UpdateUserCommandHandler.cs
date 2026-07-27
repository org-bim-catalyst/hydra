using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(IUserAdminRepository userAdminRepository) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var updated = await userAdminRepository.UpdateAsync(request.UserId, request.FirstName, request.LastName, cancellationToken);
        if (!updated)
        {
            throw new KeyNotFoundException("User not found.");
        }
    }
}
