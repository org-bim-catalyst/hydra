using MediatR;

namespace AskLucy.Application.Authentication.Commands.RequestPasswordReset;

/// <summary>
/// Asks for a reset link (specs/058-password-recovery US1). Returns nothing at all: the caller
/// gets the same neutral acknowledgement whether or not the address has an account, so there is
/// no result shape that could carry the difference back to the controller by accident (FR-003).
/// </summary>
/// <param name="Email">The address the user typed. Validated for shape only, never existence.</param>
/// <param name="RequestedFromIp">Client origin, recorded on the token for the audit trail (FR-015).</param>
public sealed record RequestPasswordResetCommand(string Email, string? RequestedFromIp) : IRequest;
