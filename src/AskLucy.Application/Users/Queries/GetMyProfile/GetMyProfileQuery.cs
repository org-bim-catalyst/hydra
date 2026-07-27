using MediatR;

namespace AskLucy.Application.Users.Queries.GetMyProfile;

public sealed record GetMyProfileQuery : IRequest<UserProfileDto>;
