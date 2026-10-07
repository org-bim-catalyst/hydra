using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Notifications.Commands.SendTemplateTest;

/// <summary>
/// Publishes a <c>template.test</c> event for the caller. It names the version to show, so the delivery pipeline renders exactly
/// what the administrator is looking at (a draft included) with sample values, and the test shows up in the deliveries like any other.
/// </summary>
public sealed class SendTemplateTestCommandHandler(
    INotificationTemplateRepository templates,
    INotificationRecipientDirectory directory,
    INotificationPublisher publisher,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<SendTemplateTestCommand, SendTemplateTestResult>
{
    public async Task<SendTemplateTestResult> Handle(SendTemplateTestCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: false, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        var version = NotificationTemplateMapper.FindVersion(template, request.VersionId);

        if (template.Channel != NotificationChannel.Email)
        {
            throw new NotificationTemplateRejectedException("Only email templates can be test-sent.");
        }

        var admin = (await directory.GetAsync([adminId], cancellationToken)).GetValueOrDefault(adminId);
        if (admin is not { IsActive: true, EmailConfirmed: true, Email: { Length: > 0 } address })
        {
            throw new NotificationTemplateRejectedException("Your account has no verified email address to send the test to.");
        }

        // Only the type's own variables travel in the event: the hub fills the standard ones itself (and the renderer adds the sample link).
        var variables = template.Definition.DeclaredVariables.ToDictionary(v => v.Name, v => (string?)v.Fallback, StringComparer.Ordinal);
        variables[TemplateSamples.TestVersionVariable] = version.Id.ToString();

        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.TemplateTest,
            new NotificationRecipient.User(adminId),
            variables,
            EventKey: $"template-test:{version.Id}:{adminId}:{timeProvider.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture)}",
            Language: template.Language));
        audit.Write(
            NotificationAuditAction.TemplateTestSent,
            "NotificationTemplateVersion",
            version.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { templateId = template.Id, type = template.Type, language = template.Language, versionNumber = version.VersionNumber });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SendTemplateTestResult(AdminAddressMask.Mask(address) ?? "your address");
    }
}
