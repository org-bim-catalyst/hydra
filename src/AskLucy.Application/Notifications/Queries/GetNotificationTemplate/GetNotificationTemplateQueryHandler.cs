using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationTemplate;

public sealed class GetNotificationTemplateQueryHandler(
    INotificationTemplateRepository templates,
    INotificationRecipientDirectory directory) : IRequestHandler<GetNotificationTemplateQuery, NotificationTemplateDetailDto>
{
    private const string SystemActor = "System";

    public async Task<NotificationTemplateDetailDto> Handle(GetNotificationTemplateQuery request, CancellationToken cancellationToken)
    {
        var template = await templates.GetWithVersionsAsync(request.TemplateId, track: false, cancellationToken)
            ?? throw new KeyNotFoundException("Template not found.");

        var authors = await directory.GetAsync(
            [.. template.Versions.Select(v => v.CreatedBy).Where(id => id.Length > 0 && !id.StartsWith("system:", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)],
            cancellationToken);

        string? AuthorOf(string id) =>
            id.StartsWith("system:", StringComparison.Ordinal) ? SystemActor
            : authors.TryGetValue(id, out var info) ? info.DisplayName ?? info.Email : null;

        return new NotificationTemplateDetailDto(
            template.Id,
            template.Type,
            template.Category,
            template.Channel,
            template.Language,
            template.Name,
            template.PublishedVersionId,
            template.IsShippedDefault,
            [
                .. template.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new TemplateVersionSummaryDto(
                    v.Id, v.VersionNumber, v.Status, v.CreatedAtUtc, AuthorOf(v.CreatedBy), v.PublishedAtUtc, v.ArchivedAtUtc)),
            ],
            [
                .. template.Definition.AllVariables.Select(v => new TemplateVariableDto(
                    v.Name, v.Fallback, v.Fallback, IsStandard: !template.Definition.DeclaredVariables.Contains(v))),
            ]);
    }
}
