using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>A rendered in-app notification: plain text only, never HTML (FR-013).</summary>
/// <param name="Title">The rendered title.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="ActionLabel">The rendered action label, if the template has one.</param>
/// <param name="TemplateVersionId">The published version that was rendered.</param>
/// <param name="Language">The language actually rendered: <c>en</c> when the requested one had no published template.</param>
public sealed record RenderedInApp(string Title, string Message, string? ActionLabel, Guid TemplateVersionId, string Language);

/// <summary>Logic-free rendering of the published template version (research R9).</summary>
public interface INotificationTemplateRenderer
{
    /// <summary>
    /// Renders the in-app template of <paramref name="definition"/>. A missing variable value takes
    /// its declared fallback and logs a warning. Throws <see cref="NotificationRenderException"/>
    /// when there is no published template or it can't be rendered.
    /// </summary>
    Task<RenderedInApp> RenderInAppAsync(
        NotificationTypeDefinition definition,
        string language,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken);
}

/// <summary>A template can't be rendered; the delivery fails with <see cref="DeliveryFailureKind.RenderError"/>.</summary>
public sealed class NotificationRenderException(string message) : Exception(message);
