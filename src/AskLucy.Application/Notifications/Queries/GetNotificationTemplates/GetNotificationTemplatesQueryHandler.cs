using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplates;

public sealed class GetNotificationTemplatesQueryHandler(INotificationTemplateRepository templates)
    : IRequestHandler<GetNotificationTemplatesQuery, IReadOnlyList<NotificationTemplateSummaryDto>>
{
    public async Task<IReadOnlyList<NotificationTemplateSummaryDto>> Handle(GetNotificationTemplatesQuery request, CancellationToken cancellationToken)
    {
        var rows = await templates.ListAsync(
            new NotificationTemplateFilter
            {
                Category = request.Category,
                Channel = request.Channel,
                Language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language.Trim(),
                Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim(),
            },
            cancellationToken);

        return
        [
            .. rows.Select(r => new NotificationTemplateSummaryDto(
                r.TemplateId,
                r.Type,
                r.Category,
                r.Channel,
                r.Language,
                r.Name,
                r.PublishedVersionId is { } id ? new TemplatePublishedVersionDto(id, r.PublishedVersionNumber ?? 0, r.PublishedAtUtc) : null,
                r.HasDraft)),
        ];
    }
}
