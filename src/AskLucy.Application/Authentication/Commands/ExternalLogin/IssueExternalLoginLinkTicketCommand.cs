using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

/// <summary>
/// Issued via a normal, JWT-authenticated XHR call before the browser navigates (top-level,
/// no Authorization header) to the OAuth challenge endpoint to link an additional provider
/// (FR-034). The resulting ticket is single-use and short-lived, and is how the challenge
/// endpoint learns which already-authenticated user to link to without needing a bearer token
/// on a plain link/redirect.
/// </summary>
public sealed record IssueExternalLoginLinkTicketCommand(string UserId) : IRequest<string>;
