using AskLucy.Application.Notifications.Templates;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.PreviewTemplateVersion;

/// <summary>
/// contracts/admin-notifications-api.md POST …/actions/preview (V). A read: it renders and sends nothing. Missing values take the
/// catalogue's samples, and sensitive links render the fixed sample link.
/// </summary>
public sealed record PreviewTemplateVersionQuery(Guid TemplateId, Guid VersionId, IReadOnlyDictionary<string, string?>? Variables = null)
    : IRequest<TemplatePreviewDto>;
