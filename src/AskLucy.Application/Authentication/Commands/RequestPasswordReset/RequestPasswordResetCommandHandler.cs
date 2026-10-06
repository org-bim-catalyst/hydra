using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;
// MediatR has its own INotificationPublisher; the hub's is the one meant here.
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.RequestPasswordReset;

/// <summary>
/// Publishes the address as typed and returns (specs/058-password-recovery US1, moved onto the hub by
/// specs/067 US9-B).
/// <para>
/// Deliberately does nothing else. Every decision about the address (does it have an account, is that
/// account confirmed, locked out, or throttled) is account-dependent work, and doing any of it here would
/// make response time reveal what the neutral 202 body is there to hide (FR-003, FR-009e). One request is
/// published, the same one for every address, and a single outbox row is inserted whether or not the address
/// belongs to anyone: the hub resolves it in the background and an address with no account ends as
/// <c>NoRecipient</c> with only a hash in the log.
/// </para>
/// </summary>
public sealed partial class RequestPasswordResetCommandHandler(
    INotificationPublisher publisher,
    IUnitOfWork unitOfWork,
    ILogger<RequestPasswordResetCommandHandler> logger) : IRequestHandler<RequestPasswordResetCommand>
{
    public async Task Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.AccountPasswordResetRequested,
            new NotificationRecipient.AddressLookup(request.Email.Trim()),
            new Dictionary<string, string?>()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The origin stays in the security log (FR-015); the address does not: it may belong to nobody.
        LogResetRequested(logger, DateTime.UtcNow, request.RequestedFromIp);
    }

    [LoggerMessage(EventId = 5806, Level = LogLevel.Information, Message = "Security: password reset requested. AtUtc={AtUtc} Origin={Origin}")]
    private static partial void LogResetRequested(ILogger logger, DateTime atUtc, string? origin);
}
