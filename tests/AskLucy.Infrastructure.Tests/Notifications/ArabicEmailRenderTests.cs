using System.Text.RegularExpressions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Notifications.Templates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// T189 — a real Arabic default rendered through the real renderer and branded shell (specs/067,
/// US8): <c>lang="ar" dir="rtl"</c>, Western digits and Gregorian dates exactly as supplied, and
/// protected names isolated in <c>&lt;bdi&gt;</c>. English output must not change.
/// </summary>
public sealed class ArabicEmailRenderTests
{
    private static readonly NotificationTypeDefinition PasswordChanged = NotificationTypeCatalog.Get(NotificationTypeKeys.SecurityPasswordChanged);
    private static readonly NotificationTypeDefinition WorkflowFailed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);

    private readonly INotificationTemplateRepository _templates = Substitute.For<INotificationTemplateRepository>();

    private LogicFreeTemplateRenderer CreateSut() =>
        new(_templates,
            new BrandedAccountEmailTemplateRenderer(Options.Create(new AppOptions { FrontendBaseUrl = "https://app.example.test" })),
            new FakeLogger<LogicFreeTemplateRenderer>());

    private void PublishSeed(NotificationTypeDefinition definition, string language)
    {
        var seed = NotificationTemplateSeeder.LoadSeeds()
            .Single(s => s.Key.Type == definition.Key && s.Key.Channel == NotificationChannel.Email && s.Key.Language == language);
        var template = NotificationTemplate.Create(definition.Key, NotificationChannel.Email, language, seed.Name, DateTime.UtcNow);
        var version = template.AddDraft(seed.Content, DateTime.UtcNow);
        template.Publish(version.Id, "test-user", DateTime.UtcNow);
        _templates.GetPublishedVersionAsync(definition.Key, NotificationChannel.Email, language, Arg.Any<CancellationToken>()).Returns(version);
    }

    [Fact]
    public async Task ArabicSeed_RendersRightToLeft_WithWesternDigitsAndAGregorianDate()
    {
        PublishSeed(PasswordChanged, "ar");
        var variables = new Dictionary<string, string?> { ["changedAt"] = "2026-10-07 09:30 UTC", ["recipientDisplayName"] = "ليلى" };

        var rendered = await CreateSut().RenderEmailAsync(PasswordChanged, "ar", variables, CancellationToken.None);

        rendered.Language.Should().Be("ar");
        rendered.HtmlBody.Should().Contain("<html lang=\"ar\" dir=\"rtl\">");
        rendered.HtmlBody.Should().Contain("2026-10-07 09:30 UTC");
        rendered.TextBody.Should().Contain("2026-10-07 09:30 UTC");
        // No Arabic-Indic (U+0660-0669) or Extended Arabic-Indic (U+06F0-06F9) digits anywhere.
        Regex.IsMatch(rendered.HtmlBody + rendered.TextBody + rendered.Subject, "[٠-٩۰-۹]").Should().BeFalse();
        // Gregorian: the year is the supplied one, not a Hijri year (~1448).
        rendered.HtmlBody.Should().NotContain("1448");
    }

    [Fact]
    public async Task ArabicSeed_WrapsEachProtectedTermInBdi()
    {
        PublishSeed(PasswordChanged, "ar");

        var rendered = await CreateSut().RenderEmailAsync(
            PasswordChanged, "ar", new Dictionary<string, string?> { ["changedAt"] = "2026-10-07" }, CancellationToken.None);

        rendered.HtmlBody.Should().Contain("<bdi>Ask Lucy</bdi>");
        rendered.TextBody.Should().NotContain("<bdi>");
    }

    [Fact]
    public async Task ArabicSeed_WrapsAProtectedTermThatArrivesThroughAVariable_AfterEncoding()
    {
        PublishSeed(WorkflowFailed, "ar");
        var variables = new Dictionary<string, string?> { ["workflowName"] = "OpenAI <b>& PDF</b>", ["failureSummary"] = "MCP timeout" };

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "ar", variables, CancellationToken.None);

        rendered.HtmlBody.Should().Contain("<bdi>OpenAI</bdi> &lt;b&gt;&amp; <bdi>PDF</bdi>&lt;/b&gt;")
            .And.Contain("<bdi>MCP</bdi> timeout")
            .And.NotContain("<b>&");
        rendered.TextBody.Should().NotContain("<bdi>", "the plain-text part carries raw values");
    }

    [Fact]
    public async Task EnglishSeed_IsNeverWrappedInBdi_AndKeepsItsOriginalRoot()
    {
        PublishSeed(PasswordChanged, "en");

        var rendered = await CreateSut().RenderEmailAsync(
            PasswordChanged, "en", new Dictionary<string, string?> { ["changedAt"] = "2026-10-07" }, CancellationToken.None);

        rendered.HtmlBody.Should().Contain("<html lang=\"en\">").And.NotContain("<bdi>").And.NotContain("dir=\"rtl\"");
    }
}
