using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.PublishTemplateVersion;

public sealed class PublishTemplateVersionCommandHandler(
    INotificationTemplateRepository templates,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<PublishTemplateVersionCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> Handle(PublishTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: true, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        var version = NotificationTemplateMapper.FindVersion(template, request.VersionId);

        NotificationTemplateMapper.EnsureDraft(version);
        NotificationTemplateMapper.EnsureCurrent(version, request.ExpectedRowVersion);
        templates.ExpectRowVersion(version, request.ExpectedRowVersion);

        var previousNumber = template.PublishedVersion?.VersionNumber;
        try
        {
            template.Publish(version.Id, adminId, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (DomainRuleViolationException ex)
        {
            throw new NotificationTemplateRejectedException(ex.Message);
        }

        audit.Write(
            NotificationAuditAction.TemplateVersionPublished,
            "NotificationTemplateVersion",
            version.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { templateId = template.Id, type = template.Type, channel = template.Channel.ToString(), language = template.Language, previousVersionNumber = previousNumber, newVersionNumber = version.VersionNumber });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NotificationTemplateMapper.ToDto(version);
    }
}
