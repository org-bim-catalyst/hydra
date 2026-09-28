using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Notifications.Templates;

/// <summary>
/// Renders the published template version with plain <c>{{ name }}</c> substitution (research R9):
/// no expressions, no conditionals, no HTML. In-app output is plain text that the client renders as
/// text, so a variable holding markup shows up literally instead of being interpreted.
/// </summary>
public sealed class LogicFreeTemplateRenderer(
    INotificationTemplateRepository templates,
    ILogger<LogicFreeTemplateRenderer> logger) : INotificationTemplateRenderer
{
    private const string FallbackLanguage = "en";
    private const char Ellipsis = '…';

    public async Task<RenderedInApp> RenderInAppAsync(
        NotificationTypeDefinition definition,
        string language,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(variables);

        var (version, renderedLanguage) = await PublishedVersionAsync(definition, NotificationChannel.InApp, language, cancellationToken);

        var valueFor = ValueResolver(definition, variables);
        var title = TemplateTokenParser.Substitute(version.Title, valueFor).Trim();
        var message = TemplateTokenParser.Substitute(version.Message, valueFor).Trim();
        var actionLabel = string.IsNullOrWhiteSpace(version.ActionLabel)
            ? null
            : TemplateTokenParser.Substitute(version.ActionLabel, valueFor).Trim();

        if (title.Length == 0 || message.Length == 0)
        {
            throw new NotificationRenderException(
                $"The published in-app template for '{definition.Key}' ({renderedLanguage}) rendered an empty title or message.");
        }

        // A long document or agent name must not push the notification past its column limits.
        return new RenderedInApp(
            Fit(title, Notification.TitleMaxLength),
            Fit(message, Notification.MessageMaxLength),
            actionLabel is { Length: > 0 } ? Fit(actionLabel, Notification.ActionLabelMaxLength) : null,
            version.Id,
            renderedLanguage);
    }

    private async Task<(NotificationTemplateVersion Version, string Language)> PublishedVersionAsync(
        NotificationTypeDefinition definition,
        NotificationChannel channel,
        string language,
        CancellationToken cancellationToken)
    {
        var version = await templates.GetPublishedVersionAsync(definition.Key, channel, language, cancellationToken);
        if (version is not null)
        {
            return (version, language);
        }

        if (!string.Equals(language, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            version = await templates.GetPublishedVersionAsync(definition.Key, channel, FallbackLanguage, cancellationToken);
            if (version is not null)
            {
                TemplateRenderLog.LanguageFellBack(logger, definition.Key, channel, language, FallbackLanguage);
                return (version, FallbackLanguage);
            }
        }

        throw new NotificationRenderException(
            $"No published {channel} template exists for '{definition.Key}' in '{language}' or '{FallbackLanguage}'.");
    }

    /// <summary>
    /// A missing value takes the variable's fallback. Standard variables are routinely absent (no
    /// display name, no link), so only a missing type-specific variable is worth a warning.
    /// </summary>
    private Func<string, string> ValueResolver(NotificationTypeDefinition definition, IReadOnlyDictionary<string, string?> variables)
    {
        var standard = NotificationTypeDefinition.StandardVariables.ToDictionary(v => v.Name, v => v.Fallback, StringComparer.Ordinal);
        var declared = definition.DeclaredVariables.ToDictionary(v => v.Name, v => v.Fallback, StringComparer.Ordinal);

        return name =>
        {
            if (variables.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (declared.TryGetValue(name, out var fallback))
            {
                TemplateRenderLog.VariableFellBack(logger, definition.Key, name);
                return fallback;
            }

            if (standard.TryGetValue(name, out fallback))
            {
                return fallback;
            }

            // Drafts are validated against the type's variables on save and publish, so this is a
            // template that bypassed validation: refuse it rather than render a guess.
            throw new NotificationRenderException($"The template for '{definition.Key}' references '{name}', which the type doesn't declare.");
        };
    }

    private static string Fit(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1).TrimEnd(), Ellipsis.ToString());
}

internal static partial class TemplateRenderLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification type {Type} rendered without a value for {Variable}; its fallback was used.")]
    public static partial void VariableFellBack(ILogger logger, string type, string variable);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification type {Type} has no published {Channel} template in {Language}; rendered in {FallbackLanguage}.")]
    public static partial void LanguageFellBack(ILogger logger, string type, NotificationChannel channel, string language, string fallbackLanguage);
}
