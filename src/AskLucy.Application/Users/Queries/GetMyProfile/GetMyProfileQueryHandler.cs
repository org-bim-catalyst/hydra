using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler(IUserProfileRepository profiles, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMyProfileQuery, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await profiles.GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Profile not found.");
    }
}
