using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>A rendered in-app notification: plain text only, never HTML (FR-013).</summary>
/// <param name="Title">The rendered title.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="ActionLabel">The rendered action label, if the template has one.</param>
/// <param name="TemplateVersionId">The published version that was rendered.</param>
/// <param name="Language">The language actually rendered: <c>en</c> when the requested one had no published template.</param>
public sealed record RenderedInApp(string Title, string Message, string? ActionLabel, Guid TemplateVersionId, string Language);

/// <summary>A rendered email: the branded HTML part and its plain-text alternative (FR-049).</summary>
/// <param name="Subject">Single-line, with CR, LF and control characters removed (FR-051).</param>
/// <param name="HtmlBody">Every variable HTML-encoded.</param>
/// <param name="TextBody">The same content, variables raw.</param>
/// <param name="TemplateVersionId">The published version that was rendered.</param>
/// <param name="Language">The language actually rendered: <c>en</c> when the requested one had no published template.</param>
public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody, Guid TemplateVersionId, string Language);

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

    /// <summary>
    /// Renders the email template of <paramref name="definition"/>. <c>variables["actionUrl"]</c>, when it
    /// is an absolute http(s) URL, becomes the one call-to-action button; the template supplies only its
    /// label. Throws <see cref="NotificationRenderException"/> when there is no published template or it
    /// can't be rendered.
    /// </summary>
    Task<RenderedEmail> RenderEmailAsync(
        NotificationTypeDefinition definition,
        string language,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken);
}

/// <summary>Renders one specific version in any status, for the admin preview (research R9).</summary>
public interface INotificationTemplatePreviewRenderer
{
    /// <summary>The in-app version rendered with <paramref name="variables"/> and the template's own language and direction.</summary>
    RenderedInApp PreviewInApp(
        NotificationTypeDefinition definition, string language, NotificationTemplateVersion version, IReadOnlyDictionary<string, string?> variables);

    /// <summary>The email version rendered through the production shell.</summary>
    RenderedEmail PreviewEmail(
        NotificationTypeDefinition definition, string language, NotificationTemplateVersion version, IReadOnlyDictionary<string, string?> variables);

    /// <summary><c>ltr</c> or <c>rtl</c> for a language.</summary>
    string DirectionOf(string language);
}

/// <summary>A template can't be rendered; the delivery fails with <see cref="DeliveryFailureKind.RenderError"/>.</summary>
public sealed class NotificationRenderException(string message) : Exception(message);
