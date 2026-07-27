using MediatR;

namespace AskLucy.Application.Users.Commands.UpdateUser;

/// <summary>
/// Admin update of another user's profile. Deliberately carries only allow-listed
/// fields — no raw entity, no id/role/password — closing the legacy
/// overposting/mass-assignment vulnerability (spec.md &#167; Gap Analysis).
/// </summary>
public sealed record UpdateUserCommand(string UserId, string? FirstName, string? LastName) : IRequest;
