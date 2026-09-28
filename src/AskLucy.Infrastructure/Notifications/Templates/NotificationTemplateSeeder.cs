using System.Reflection;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Notifications.Templates;

/// <summary>
/// Installs the shipped default templates at startup (research R9): a published v1 for every
/// embedded <c>Seed/{language}/{type}.{channel}.json</c> whose template doesn't exist yet. It never
/// overwrites, so administrator edits survive every deploy. A failure is logged at Error and doesn't
/// stop the host; the affected notifications then fail to render with a visible render error.
/// </summary>
public sealed class NotificationTemplateSeeder(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<NotificationTemplateSeeder> logger) : IHostedService
{
    /// <summary>The <c>LogicalName</c> prefix the csproj gives the embedded seed files.</summary>
    public const string ResourcePrefix = "NotificationSeed/";

    public const string SeederUserId = "system:template-seeder";

    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TemplateSeedLog.SeedingFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Every shipped seed file, parsed. Public so a test can check them against the catalogue.</summary>
    public static IReadOnlyList<TemplateSeed> LoadSeeds()
    {
        var assembly = typeof(NotificationTemplateSeeder).Assembly;
        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => Load(assembly, name))];
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<INotificationTemplateRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var existing = await templates.GetExistingKeysAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var added = 0;

        foreach (var seed in LoadSeeds())
        {
            if (existing.Contains(seed.Key))
            {
                continue;
            }

            try
            {
                var template = NotificationTemplate.Create(seed.Key.Type, seed.Key.Channel, seed.Key.Language, seed.Name, now);
                var version = template.AddDraft(seed.Content, now);
                template.Publish(version.Id, SeederUserId, now);
                templates.Add(template);
                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad file must not keep every other type from getting its template.
                TemplateSeedLog.SeedRejected(logger, ex, seed.ResourceName);
            }
        }

        if (added > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            TemplateSeedLog.Seeded(logger, added);
        }
    }

    private static TemplateSeed Load(Assembly assembly, string resourceName)
    {
        // "NotificationSeed/en/document.processing.completed.inapp.json"; RecursiveDir keeps the
        // build machine's separator, so accept either.
        var relative = resourceName[ResourcePrefix.Length..].Replace('\\', '/');
        var slash = relative.IndexOf('/', StringComparison.Ordinal);
        var language = slash > 0 ? relative[..slash] : throw InvalidName(resourceName);

        var parts = relative[(slash + 1)..].Split('.');
        if (parts.Length < 3 || !string.Equals(parts[^1], "json", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidName(resourceName);
        }

        var channel = parts[^2].ToLowerInvariant() switch
        {
            "inapp" => NotificationChannel.InApp,
            "email" => NotificationChannel.Email,
            _ => throw InvalidName(resourceName),
        };
        var type = string.Join('.', parts[..^2]);

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded seed '{resourceName}' couldn't be opened.");
        var file = JsonSerializer.Deserialize<SeedFile>(stream, SeedJson)
            ?? throw new InvalidOperationException($"Embedded seed '{resourceName}' is empty.");

        var content = new NotificationTemplateContent
        {
            Subject = file.Subject,
            Preheader = file.Preheader,
            Greeting = file.Greeting,
            Heading = file.Heading,
            BodyParagraphs = file.BodyParagraphs ?? [],
            SafetyNote = file.SafetyNote,
            FooterNote = file.FooterNote,
            Title = file.Title,
            Message = file.Message,
            ActionLabel = file.ActionLabel,
        };

        return new TemplateSeed(resourceName, new NotificationTemplateKey(type, channel, language), file.Name, content);
    }

    private static InvalidOperationException InvalidName(string resourceName) =>
        new($"Embedded seed '{resourceName}' isn't named Seed/{{language}}/{{type}}.{{inapp|email}}.json.");

    private sealed record SeedFile(
        string Name,
        string? Title,
        string? Message,
        string? ActionLabel,
        string? Subject,
        string? Preheader,
        string? Greeting,
        string? Heading,
        IReadOnlyList<string>? BodyParagraphs,
        string? SafetyNote,
        string? FooterNote);
}

/// <summary>One shipped default template.</summary>
public sealed record TemplateSeed(string ResourceName, NotificationTemplateKey Key, string Name, NotificationTemplateContent Content);

internal static partial class TemplateSeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {Count} default notification template(s).")]
    public static partial void Seeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Default notification template {ResourceName} was rejected and not installed.")]
    public static partial void SeedRejected(ILogger logger, Exception exception, string resourceName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Installing the default notification templates failed; notifications without a template will fail to render.")]
    public static partial void SeedingFailed(ILogger logger, Exception exception);
}
