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
/// T105 — the email path of <see cref="LogicFreeTemplateRenderer"/> over the real branded shell
/// (research R9, FR-041, FR-042, FR-049 to FR-051): a variable is HTML-encoded in the HTML part and raw in
/// the text part, a subject is one clean line, <c>lang</c> and <c>dir</c> reach the root element, a
/// minimized type renders only what it declares, and a token the type doesn't declare is a render error.
/// </summary>
public sealed class EmailTemplateRenderTests
{
    private static readonly NotificationTypeDefinition WorkflowFailed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);
    private static readonly NotificationTypeDefinition PasswordChanged = NotificationTypeCatalog.Get(NotificationTypeKeys.SecurityPasswordChanged);

    private readonly INotificationTemplateRepository _templates = Substitute.For<INotificationTemplateRepository>();
    private readonly FakeLogger<LogicFreeTemplateRenderer> _logger = new();

    private LogicFreeTemplateRenderer CreateSut() =>
        new(_templates, new BrandedAccountEmailTemplateRenderer(Options.Create(new AppOptions { FrontendBaseUrl = "https://app.example.test" })), _logger);

    private static NotificationTemplateVersion PublishedVersion(
        string type, NotificationTemplateContent content, string language = "en")
    {
        var template = NotificationTemplate.Create(type, NotificationChannel.Email, language, "Test", DateTime.UtcNow);
        var version = template.AddDraft(content, DateTime.UtcNow);
        template.Publish(version.Id, "test-user", DateTime.UtcNow);
        return version;
    }

    private void Publish(NotificationTypeDefinition definition, NotificationTemplateVersion version, string language = "en") =>
        _templates.GetPublishedVersionAsync(definition.Key, NotificationChannel.Email, language, Arg.Any<CancellationToken>()).Returns(version);

    private static NotificationTemplateContent WorkflowTemplate(string subject = "Workflow failed: {{ workflowName }}") => new()
    {
        Subject = subject,
        Preheader = "Your workflow needs attention.",
        Greeting = "Hi {{ recipientDisplayName }},",
        Heading = "Workflow failed",
        BodyParagraphs = ["{{ workflowName }} couldn't finish: {{ failureSummary }}"],
        SafetyNote = "You can change which emails you receive in Settings.",
        ActionLabel = "View run",
    };

    [Fact]
    public async Task RenderEmailAsync_EncodesVariablesInTheHtml_AndLeavesThemRawInTheText()
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));
        var variables = new Dictionary<string, string?>
        {
            ["workflowName"] = "<b>Tom & \"Jerry\"</b>",
            ["failureSummary"] = "<script>alert(1)</script>",
        };

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", variables, CancellationToken.None);

        rendered.HtmlBody.Should().Contain("&lt;b&gt;Tom &amp; &quot;Jerry&quot;&lt;/b&gt;")
            .And.Contain("&lt;script&gt;alert(1)&lt;/script&gt;")
            .And.NotContain("<script>")
            .And.NotContain("<b>Tom");
        rendered.TextBody.Should().Contain("<b>Tom & \"Jerry\"</b> couldn't finish: <script>alert(1)</script>");
    }

    [Fact]
    public async Task RenderEmailAsync_StripsLineBreaksAndControlCharactersFromTheSubject()
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));
        var variables = new Dictionary<string, string?> { ["workflowName"] = "Nightly\r\nBcc: attacker@example.com\0\u2028\u0007 build" };

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", variables, CancellationToken.None);

        rendered.Subject.Should().NotContainAny("\r", "\n", "\0", "\u0007", "\u2028");
        rendered.Subject.Should().Be("Workflow failed: Nightly Bcc: attacker@example.com build");
    }

    [Fact]
    public async Task RenderEmailAsync_SetsLangAndDirOnTheRoot_ForARightToLeftLanguage()
    {
        var arabic = PublishedVersion(WorkflowFailed.Key, WorkflowTemplate("فشل سير العمل: {{ workflowName }}"), "ar");
        Publish(WorkflowFailed, arabic, "ar");

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "ar", new Dictionary<string, string?> { ["workflowName"] = "X" }, CancellationToken.None);

        rendered.Language.Should().Be("ar");
        rendered.HtmlBody.Should().Contain("<html lang=\"ar\" dir=\"rtl\">");
    }

    [Fact]
    public async Task RenderEmailAsync_KeepsTheOriginalRootForEnglish()
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", new Dictionary<string, string?>(), CancellationToken.None);

        rendered.HtmlBody.Should().Contain("<html lang=\"en\">");
    }

    [Fact]
    public async Task RenderEmailAsync_FallsBackToEnglish_WhenTheRequestedLanguageHasNoPublishedTemplate()
    {
        _templates.GetPublishedVersionAsync(WorkflowFailed.Key, NotificationChannel.Email, "ar", Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateVersion?)null);
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "ar", new Dictionary<string, string?>(), CancellationToken.None);

        rendered.Language.Should().Be("en");
        rendered.HtmlBody.Should().Contain("<html lang=\"en\">");
    }

    [Fact]
    public async Task RenderEmailAsync_ForAMinimizedType_RendersOnlyItsDeclaredVariables()
    {
        var content = new NotificationTemplateContent
        {
            Subject = "Your password was changed",
            Greeting = "Hi {{ recipientDisplayName }},",
            Heading = "Your password was changed",
            BodyParagraphs = ["Your password was changed on {{ changedAt }}."],
            SafetyNote = "If this wasn't you, reset your password now.",
        };
        Publish(PasswordChanged, PublishedVersion(PasswordChanged.Key, content));
        var variables = new Dictionary<string, string?>
        {
            ["changedAt"] = "2026-10-06 09:00 UTC",
            ["recipientEmail"] = "jane@example.com",
            ["deviceFingerprint"] = "abc123-secret",
        };

        var rendered = await CreateSut().RenderEmailAsync(PasswordChanged, "en", variables, CancellationToken.None);

        rendered.TextBody.Should().Contain("2026-10-06 09:00 UTC");
        rendered.HtmlBody.Should().NotContain("jane@example.com").And.NotContain("abc123-secret");
        rendered.TextBody.Should().NotContain("jane@example.com").And.NotContain("abc123-secret");
    }

    [Fact]
    public async Task RenderEmailAsync_ForAMinimizedType_RefusesATemplateThatReferencesAnUndeclaredVariable()
    {
        // A draft can't be saved with such a token, so the stored version comes from a type that allows any.
        var smuggled = PublishedVersion(NotificationTypeKeys.TemplateTest, new NotificationTemplateContent
        {
            Subject = "Hello",
            Heading = "Hello",
            BodyParagraphs = ["Code: {{ recoveryCode }}"],
            SafetyNote = "Safe.",
        });
        Publish(PasswordChanged, smuggled);

        var act = () => CreateSut().RenderEmailAsync(
            PasswordChanged, "en", new Dictionary<string, string?> { ["recoveryCode"] = "123456" }, CancellationToken.None);

        (await act.Should().ThrowAsync<NotificationRenderException>()).Which.Message.Should().Contain("recoveryCode");
    }

    [Fact]
    public async Task RenderEmailAsync_UnknownToken_IsARenderError()
    {
        var smuggled = PublishedVersion(NotificationTypeKeys.TemplateTest, new NotificationTemplateContent
        {
            Subject = "Hello {{ secretToken }}",
            Heading = "Hello",
            BodyParagraphs = ["Body."],
            SafetyNote = "Safe.",
        });
        Publish(WorkflowFailed, smuggled);

        var act = () => CreateSut().RenderEmailAsync(WorkflowFailed, "en", new Dictionary<string, string?>(), CancellationToken.None);

        (await act.Should().ThrowAsync<NotificationRenderException>()).Which.Message.Should().Contain("secretToken");
    }

    [Fact]
    public async Task RenderEmailAsync_NoPublishedTemplate_IsARenderError()
    {
        _templates.GetPublishedVersionAsync(Arg.Any<string>(), NotificationChannel.Email, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateVersion?)null);

        var act = () => CreateSut().RenderEmailAsync(WorkflowFailed, "en", new Dictionary<string, string?>(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationRenderException>();
    }

    [Fact]
    public async Task RenderEmailAsync_AMissingVariableUsesItsFallback_AndLogsAWarning()
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", new Dictionary<string, string?>(), CancellationToken.None);

        rendered.TextBody.Should().Contain("your workflow couldn't finish: an unexpected error");
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning && r.Message.Contains("workflowName"));
    }

    [Fact]
    public async Task RenderEmailAsync_AnAbsoluteActionUrl_BecomesTheButton()
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));
        var variables = new Dictionary<string, string?> { ["actionUrl"] = "https://app.example.test/workflows/w1/executions/e1" };

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", variables, CancellationToken.None);

        rendered.HtmlBody.Should().Contain("href=\"https://app.example.test/workflows/w1/executions/e1\"").And.Contain("View run");
        rendered.TextBody.Should().Contain("View run: https://app.example.test/workflows/w1/executions/e1");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public async Task RenderEmailAsync_ANonWebActionUrl_IsNeverPutInAButton(string actionUrl)
    {
        Publish(WorkflowFailed, PublishedVersion(WorkflowFailed.Key, WorkflowTemplate()));
        var variables = new Dictionary<string, string?> { ["actionUrl"] = actionUrl };

        var rendered = await CreateSut().RenderEmailAsync(WorkflowFailed, "en", variables, CancellationToken.None);

        rendered.HtmlBody.Should().NotContain("href=\"javascript").And.NotContain("href=\"/relative").And.NotContain("href=\"data:");
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }
}
