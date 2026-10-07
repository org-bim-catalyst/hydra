using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplateVersion;

public sealed class GetNotificationTemplateVersionQueryHandler(INotificationTemplateRepository templates)
    : IRequestHandler<GetNotificationTemplateVersionQuery, TemplateVersionDto>
{
    public async Task<TemplateVersionDto> Handle(GetNotificationTemplateVersionQuery request, CancellationToken cancellationToken)
    {
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: false, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");
        return NotificationTemplateMapper.ToDto(NotificationTemplateMapper.FindVersion(template, request.VersionId));
    }
}
