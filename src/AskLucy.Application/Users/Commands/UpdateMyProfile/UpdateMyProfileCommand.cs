using MediatR;

namespace AskLucy.Application.Users.Commands.UpdateMyProfile;

public sealed record UpdateMyProfileCommand(string? FirstName, string? LastName) : IRequest;
