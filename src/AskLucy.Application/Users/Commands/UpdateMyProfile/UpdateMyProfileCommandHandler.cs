using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.UpdateMyProfile;

/// <summary>Scoped to "me" only — a user can never update another user's profile (FR-018).</summary>
public sealed class UpdateMyProfileCommandHandler(IUserProfileRepository profiles, ICurrentUserAccessor currentUser)
    : IRequestHandler<UpdateMyProfileCommand>
{
    public async Task Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        await profiles.UpdateAsync(userId, request.FirstName, request.LastName, cancellationToken);
    }
}
