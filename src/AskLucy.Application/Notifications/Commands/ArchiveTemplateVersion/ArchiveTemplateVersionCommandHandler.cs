using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.ArchiveTemplateVersion;

public sealed class ArchiveTemplateVersionCommandHandler(
    INotificationTemplateRepository templates,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<ArchiveTemplateVersionCommand, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> Handle(ArchiveTemplateVersionCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: true, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        var version = NotificationTemplateMapper.FindVersion(template, request.VersionId);

        if (version.Status == TemplateVersionStatus.Archived)
        {
            throw new NotificationTemplateConflictException(TemplateConflictReason.VersionArchived, "This version is already archived.");
        }

        NotificationTemplateMapper.EnsureCurrent(version, request.ExpectedRowVersion);
        if (version.Status == TemplateVersionStatus.Published && template.IsShippedDefault)
        {
            throw new NotificationTemplateConflictException(
                TemplateConflictReason.LastPublishedDefault,
                "This template must always have a published version. Publish a replacement instead.");
        }

        templates.ExpectRowVersion(version, request.ExpectedRowVersion);
        template.Archive(version.Id, adminId, timeProvider.GetUtcNow().UtcDateTime);

        audit.Write(
            NotificationAuditAction.TemplateVersionArchived,
            "NotificationTemplateVersion",
            version.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { templateId = template.Id, type = template.Type, channel = template.Channel.ToString(), language = template.Language, versionNumber = version.VersionNumber });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NotificationTemplateMapper.ToDto(version);
    }
}
