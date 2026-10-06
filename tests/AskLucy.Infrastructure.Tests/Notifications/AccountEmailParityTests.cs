using System.Net;
using AskLucy.Application.Abstractions;
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
/// T124 — specs/067 US9-B: moving the account emails onto the hub must not change what the reader sees. For
/// each account type, the subject, heading and body paragraphs of the seeded hub template are compared with
/// what <see cref="BrandedAccountEmailTemplateRenderer"/> produced for the same inputs when the handlers and
/// jobs built the content themselves. The comparison is on the subject and the plain-text part, which is the
/// whole content with no markup: the HTML differs only in encoding of an apostrophe and in one inline style.
/// </summary>
public sealed class AccountEmailParityTests
{
    private const string Link = "https://app.example.test/confirm-email?userId=u1&token=T%2B1";
    private const string Address = "layla@example.com";

    private static readonly BrandedAccountEmailTemplateRenderer Legacy =
        new(Options.Create(new AppOptions { FrontendBaseUrl = "https://app.example.test" }));

    private readonly INotificationTemplateRepository _templates = Substitute.For<INotificationTemplateRepository>();

    private LogicFreeTemplateRenderer CreateSut() => new(_templates, Legacy, new FakeLogger<LogicFreeTemplateRenderer>());

    /// <summary>The seed shipped for <paramref name="type"/>, published as the hub would have it after the seeder ran.</summary>
    private void PublishSeed(string type)
    {
        var seed = NotificationTemplateSeeder.LoadSeeds().Single(s => s.Key == new NotificationTemplateKey(type, NotificationChannel.Email, "en"));
        var template = NotificationTemplate.Create(type, NotificationChannel.Email, "en", seed.Name, DateTime.UtcNow);
        var version = template.AddDraft(seed.Content, DateTime.UtcNow);
        template.Publish(version.Id, "test", DateTime.UtcNow);
        _templates.GetPublishedVersionAsync(type, NotificationChannel.Email, "en", Arg.Any<CancellationToken>()).Returns(version);
    }

    private async Task<(string Subject, string Text, string Html)> HubAsync(string type, Dictionary<string, string?> variables)
    {
        PublishSeed(type);
        var rendered = await CreateSut().RenderEmailAsync(NotificationTypeCatalog.Get(type), "en", variables, CancellationToken.None);
        return (rendered.Subject, rendered.TextBody, rendered.HtmlBody);
    }

    [Fact]
    public async Task EmailConfirmation_MatchesTheRegisterHandlersEmail()
    {
        // RegisterCommandHandler built this inline.
        var legacy = Legacy.Render(new AccountEmailContent(
            Subject: "Confirm your Ask Lucy account",
            PreheaderText: "Confirm your email to finish setting up your Ask Lucy account.",
            Heading: "Confirm your account",
            BodyParagraphs: [$"Hi {WebUtility.HtmlEncode(Address)},", "Please confirm your Ask Lucy account by clicking the button below."],
            SafetyNote: "If you didn't create an Ask Lucy account, you can safely ignore this email.",
            PrimaryAction: new EmailAction("Confirm my email", Link)));

        var hub = await HubAsync(NotificationTypeKeys.AccountEmailConfirmationRequested,
            new() { ["recipientDisplayName"] = Address, ["actionUrl"] = Link });

        hub.Subject.Should().Be("Confirm your Ask Lucy account");
        hub.Text.Should().Be(legacy.TextBody);
        hub.Html.Should().Contain("Confirm my email").And.Contain("href=\"https://app.example.test/confirm-email?userId=u1&amp;token=T%2B1\"");
    }

    [Fact]
    public async Task EmailChange_MatchesTheRequestEmailChangeHandlersEmail()
    {
        var legacy = Legacy.Render(new AccountEmailContent(
            Subject: "Confirm your new Ask Lucy email",
            PreheaderText: "Confirm this address to finish changing your Ask Lucy account email.",
            Heading: "Confirm your new email address",
            BodyParagraphs: ["You requested to change your Ask Lucy account email to this address."],
            SafetyNote: "If you didn't request this, you can safely ignore this message.",
            PrimaryAction: new EmailAction("Confirm email change", Link)));

        var hub = await HubAsync(NotificationTypeKeys.AccountEmailChangeRequested,
            new() { ["newEmailMasked"] = "n***@example.com", ["actionUrl"] = Link });

        hub.Subject.Should().Be("Confirm your new Ask Lucy email");
        hub.Text.Should().Be(legacy.TextBody);
    }

    [Fact]
    public async Task PasswordReset_MatchesThePasswordEmailJobsEmail()
    {
        var legacy = Legacy.Render(new AccountEmailContent(
            Subject: "Reset your Ask Lucy password",
            PreheaderText: "Use this link to choose a new Ask Lucy password.",
            Heading: "Reset your password",
            BodyParagraphs: [$"Hi {WebUtility.HtmlEncode(Address)},", "We received a request to reset your Ask Lucy password. Use the button below to choose a new one."],
            SafetyNote: "This link expires in one hour and can be used once. If you did not ask for it, you can ignore this email — your password has not changed.",
            PrimaryAction: new EmailAction("Reset my password", Link)));

        var hub = await HubAsync(NotificationTypeKeys.AccountPasswordResetRequested,
            new() { ["recipientDisplayName"] = Address, ["actionUrl"] = Link });

        hub.Subject.Should().Be("Reset your Ask Lucy password");
        hub.Text.Should().Be(legacy.TextBody);
    }

    [Fact]
    public async Task PasswordChanged_MatchesThePasswordEmailJobsNotice()
    {
        var changedAt = new DateTime(2026, 10, 6, 9, 30, 0, DateTimeKind.Utc);
        var legacy = Legacy.Render(new AccountEmailContent(
            Subject: "Your Ask Lucy password was changed",
            PreheaderText: "Your Ask Lucy password was just changed.",
            Heading: "Your password was changed",
            BodyParagraphs: [$"Hi {WebUtility.HtmlEncode(Address)},", $"Your Ask Lucy password was changed on {changedAt:yyyy-MM-dd HH:mm} UTC."],
            SafetyNote: "If this was you, nothing further is needed. If it was not, reset your password immediately and review your active sessions."));

        var hub = await HubAsync(NotificationTypeKeys.SecurityPasswordChanged,
            new() { ["recipientDisplayName"] = Address, ["changedAt"] = "2026-10-06 09:30 UTC" });

        hub.Subject.Should().Be("Your Ask Lucy password was changed");
        hub.Text.Should().Be(legacy.TextBody);
        hub.Text.Should().NotContain("http", "a password-changed notice carries no link");
    }

    [Fact]
    public async Task SupportRequest_CarriesEveryFieldTheRelayedEmailDid_WithTheMessageEncodedInTheHtml()
    {
        var hub = await HubAsync(NotificationTypeKeys.AccountSupportRequestSubmitted, new()
        {
            ["requesterEmail"] = Address,
            ["messageBody"] = "I'm locked out <b>please</b> help & thanks",
            ["occurredAt"] = "2026-10-06 09:30 UTC",
        });

        hub.Subject.Should().Be("Account access request");
        hub.Text.Should().Contain("A signed-out user asked for help getting back into their account.")
            .And.Contain($"Account: {Address}")
            .And.Contain("Message: I'm locked out <b>please</b> help & thanks");
        hub.Html.Should().Contain("&lt;b&gt;please&lt;/b&gt;").And.NotContain("<b>please</b>");
    }
}
