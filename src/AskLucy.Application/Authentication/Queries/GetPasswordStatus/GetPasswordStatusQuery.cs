using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetPasswordStatus;

/// <summary>Whether the account has a password at all (specs/058-password-recovery FR-014).</summary>
public sealed record GetPasswordStatusQuery(string UserId) : IRequest<bool>;
