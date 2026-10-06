using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Notifications.Templates;

/// <summary>
/// Renders the published template version with plain <c>{{ name }}</c> substitution (research R9):
/// no expressions, no conditionals, no HTML. In-app output is plain text that the client renders as
/// text, so a variable holding markup shows up literally instead of being interpreted. Email output
/// substitutes first and encodes after, so a variable can never inject markup into the HTML part, and
/// the plain-text part carries the same values raw.
/// </summary>
public sealed class LogicFreeTemplateRenderer(
    INotificationTemplateRepository templates,
    IEmailTemplateRenderer emailShell,
    ILogger<LogicFreeTemplateRenderer> logger) : INotificationTemplateRenderer
{
    private const string FallbackLanguage = "en";
    private const char Ellipsis = '…';

    /// <summary>Languages written right to left; everything else renders left to right.</summary>
    private static readonly HashSet<string> RightToLeftLanguages = new(StringComparer.OrdinalIgnoreCase) { "ar", "he", "fa", "ur" };

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

    public async Task<RenderedEmail> RenderEmailAsync(
        NotificationTypeDefinition definition,
        string language,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(variables);

        var (version, renderedLanguage) = await PublishedVersionAsync(definition, NotificationChannel.Email, language, cancellationToken);

        // A minimized type (security, FR-025) renders only what it declares; anything else a caller
        // slipped into the dictionary is dropped before substitution, not merely left unreferenced.
        var allowed = definition.MinimizeSensitiveContent ? OnlyAllowed(definition, variables) : variables;
        var valueFor = ValueResolver(definition, allowed);
        string Plain(string? text) => TemplateTokenParser.Substitute(text, valueFor).Trim();

        var subject = SingleLine(Plain(version.Subject));
        var heading = Plain(version.Heading);
        var paragraphs = version.BodyParagraphs.Select(Plain).Where(p => p.Length > 0).ToList();
        var safetyNote = Plain(version.SafetyNote);
        if (subject.Length == 0 || heading.Length == 0 || paragraphs.Count == 0 || safetyNote.Length == 0)
        {
            throw new NotificationRenderException(
                $"The published email template for '{definition.Key}' ({renderedLanguage}) rendered an empty subject, heading, body or safety note.");
        }

        var greeting = Plain(version.Greeting);
        var footer = Plain(version.FooterNote);
        var preheader = Plain(version.Preheader);
        var actionLabel = Plain(version.ActionLabel);

        EmailAction? action = null;
        if (actionLabel.Length > 0)
        {
            var url = allowed.GetValueOrDefault(TemplateTokenParser.ActionUrlVariable);
            if (IsAbsoluteWebUrl(url))
            {
                action = new EmailAction(actionLabel, url!);
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                // Never put a non-http(s) or relative target in a button (FR-047); the email goes out without one.
                TemplateRenderLog.ActionUrlRejected(logger, definition.Key);
            }
        }

        // The branded shell writes these into HTML as given, so every substituted text is encoded here
        // exactly once. Subject, preheader and the button are encoded by the shell itself.
        var content = new AccountEmailContent(
            Subject: subject,
            PreheaderText: preheader.Length > 0 ? preheader : subject,
            Heading: Encode(heading),
            BodyParagraphs: [.. paragraphs.Select(Encode)],
            SafetyNote: Encode(safetyNote),
            Greeting: greeting.Length > 0 ? Encode(greeting) : null,
            PrimaryAction: action,
            FooterNote: footer.Length > 0 ? Encode(footer) : null);

        var (html, text) = emailShell.Render(content, renderedLanguage, DirectionOf(renderedLanguage));
        return new RenderedEmail(subject, html, text, version.Id, renderedLanguage);
    }

    /// <summary>Drops CR, LF, every control character and the Unicode line separators, then collapses runs of whitespace (FR-051).</summary>
    internal static string SingleLine(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var c in value)
        {
            var isBreak = char.IsControl(c) || c is '\u2028' or '\u2029';
            if (isBreak || char.IsWhiteSpace(c))
            {
                // A line break inside a subject would otherwise split the header (header injection).
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text) ?? string.Empty;

    internal static string DirectionOf(string language)
    {
        var primary = language.Split('-', 2)[0];
        return RightToLeftLanguages.Contains(primary) ? "rtl" : "ltr";
    }

    private static bool IsAbsoluteWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static Dictionary<string, string?> OnlyAllowed(NotificationTypeDefinition definition, IReadOnlyDictionary<string, string?> variables) =>
        variables.Where(v => definition.Declares(v.Key) || v.Key == TemplateTokenParser.ActionUrlVariable)
            .ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification type {Type} had an email action URL that isn't an absolute http(s) URL; the email was rendered without its button.")]
    public static partial void ActionUrlRejected(ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification type {Type} has no published {Channel} template in {Language}; rendered in {FallbackLanguage}.")]
    public static partial void LanguageFellBack(ILogger logger, string type, NotificationChannel channel, string language, string fallbackLanguage);
}
