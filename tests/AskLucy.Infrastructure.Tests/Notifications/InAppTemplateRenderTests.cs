using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications.Templates;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>T011 — <see cref="LogicFreeTemplateRenderer"/> (research R9): plain-text substitution, the
/// language fallback to "en", and per-variable fallbacks, each logged rather than thrown where the
/// spec calls for graceful degradation.</summary>
public sealed class InAppTemplateRenderTests
{
    private static readonly NotificationTypeDefinition Definition = NotificationTypeCatalog.Get(NotificationTypeKeys.DocumentProcessingCompleted);

    private readonly INotificationTemplateRepository _templates = Substitute.For<INotificationTemplateRepository>();

    private LogicFreeTemplateRenderer CreateSut(FakeLogger<LogicFreeTemplateRenderer>? logger = null) =>
        new(_templates, Substitute.For<AskLucy.Application.Abstractions.IEmailTemplateRenderer>(), logger ?? new FakeLogger<LogicFreeTemplateRenderer>());

    private static NotificationTemplateVersion PublishedVersion(NotificationTemplateContent content, NotificationChannel channel = NotificationChannel.InApp)
    {
        var template = NotificationTemplate.Create(Definition.Key, channel, "en", "Test", DateTime.UtcNow);
        var version = template.AddDraft(content, DateTime.UtcNow);
        template.Publish(version.Id, "test-user", DateTime.UtcNow);
        return version;
    }

    [Fact]
    public async Task RenderInAppAsync_ShouldSubstitutePlainText_WithoutInterpretingMarkup()
    {
        var version = PublishedVersion(new NotificationTemplateContent
        {
            Title = "Document ready",
            Message = "\"{{ documentName }}\" finished processing.",
        });
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, "en", Arg.Any<CancellationToken>())
            .Returns(version);
        var sut = CreateSut();

        var rendered = await sut.RenderInAppAsync(
            Definition, "en", new Dictionary<string, string?> { ["documentName"] = "<b>Plans.pdf</b>" }, CancellationToken.None);

        rendered.Title.Should().Be("Document ready");
        rendered.Message.Should().Be("\"<b>Plans.pdf</b>\" finished processing.", "the renderer is logic-free: markup in a value is never interpreted, only substituted literally");
        rendered.Language.Should().Be("en");
    }

    [Fact]
    public async Task RenderInAppAsync_ShouldFallBackToEnglish_AndLogTheFallback_WhenTheRequestedLanguageHasNoPublishedTemplate()
    {
        var version = PublishedVersion(new NotificationTemplateContent { Title = "Document ready", Message = "\"{{ documentName }}\" finished processing." });
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, "ar", Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateVersion?)null);
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, "en", Arg.Any<CancellationToken>())
            .Returns(version);
        var logger = new FakeLogger<LogicFreeTemplateRenderer>();
        var sut = CreateSut(logger);

        var rendered = await sut.RenderInAppAsync(
            Definition, "ar", new Dictionary<string, string?> { ["documentName"] = "Plans.pdf" }, CancellationToken.None);

        rendered.Language.Should().Be("en");
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information)
            .Which.Message.Should().Contain("ar").And.Contain("en");
    }

    [Fact]
    public async Task RenderInAppAsync_ShouldUseTheDeclaredFallback_AndLogAWarning_WhenAVariableIsMissing()
    {
        var version = PublishedVersion(new NotificationTemplateContent { Title = "Document ready", Message = "\"{{ documentName }}\" finished processing." });
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, "en", Arg.Any<CancellationToken>())
            .Returns(version);
        var logger = new FakeLogger<LogicFreeTemplateRenderer>();
        var sut = CreateSut(logger);

        var rendered = await sut.RenderInAppAsync(
            Definition, "en", new Dictionary<string, string?>(), CancellationToken.None);

        rendered.Message.Should().Be("\"your document\" finished processing.", "the type's declared fallback for documentName");
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("documentName");
    }

    [Fact]
    public async Task RenderInAppAsync_ShouldThrow_WhenNeitherTheRequestedLanguageNorEnglishHasAPublishedTemplate()
    {
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateVersion?)null);
        var sut = CreateSut();

        var act = () => sut.RenderInAppAsync(Definition, "fr", new Dictionary<string, string?>(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationRenderException>();
    }

    [Fact]
    public async Task RenderInAppAsync_ShouldTruncateWithAnEllipsis_WhenTheRenderedTextExceedsTheColumnLimit()
    {
        var longName = new string('x', Notification.MessageMaxLength + 50);
        var version = PublishedVersion(new NotificationTemplateContent { Title = "Document ready", Message = "{{ documentName }}" });
        _templates.GetPublishedVersionAsync(Definition.Key, NotificationChannel.InApp, "en", Arg.Any<CancellationToken>())
            .Returns(version);
        var sut = CreateSut();

        var rendered = await sut.RenderInAppAsync(
            Definition, "en", new Dictionary<string, string?> { ["documentName"] = longName }, CancellationToken.None);

        rendered.Message.Length.Should().Be(Notification.MessageMaxLength);
        rendered.Message.Should().EndWith("…");
    }
}
