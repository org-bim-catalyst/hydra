using MediatR;

namespace AskLucy.Application.Authentication.Commands.RequestAccountSupport;

/// <summary>
/// A locked-out user's message to the support mailbox, raised from the sign-in page. The
/// destination address is server-side configuration and is never returned to the caller.
/// </summary>
public sealed record RequestAccountSupportCommand(string Email, string Message, string? RequestedFromIp) : IRequest;
