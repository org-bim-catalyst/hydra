using System.Linq;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>specs/061-branded-email-templates T006/T016/T018/T019.</summary>
public sealed class BrandedAccountEmailTemplateRendererTests
{
    private readonly BrandedAccountEmailTemplateRenderer _renderer =
        new(Options.Create(new AppOptions { FrontendBaseUrl = "https://asklucy.io" }));

    private static AccountEmailContent ContentWithAction() => new(
        Subject: "Confirm your Ask Lucy account",
        PreheaderText: "Confirm your email to finish setting up your Ask Lucy account.",
        Heading: "Confirm your account",
        BodyParagraphs: ["Hi Ada,", "Please confirm your Ask Lucy account by clicking the button below."],
        SafetyNote: "If you didn't create an Ask Lucy account, you can safely ignore this email.",
        Greeting: "Welcome aboard,",
        PrimaryAction: new EmailAction("Confirm my email", "https://asklucy.io/confirm-email?userId=user-1&token=abc"),
        FooterNote: "This link expires in 24 hours.");

    private static AccountEmailContent ContentWithoutAction() => new(
        Subject: "Your Ask Lucy password was changed",
        PreheaderText: "Your Ask Lucy password was just changed.",
        Heading: "Your password was changed",
        BodyParagraphs: ["Hi user@example.com,", "Your Ask Lucy password was changed on 2026-09-20 10:00 UTC."],
        SafetyNote: "If this was you, nothing further is needed. If it was not, reset your password immediately.");

    public static TheoryData<AccountEmailContent> AllContentVariants() =>
        new()
        {
            ContentWithAction(),
            ContentWithoutAction(),
            new AccountEmailContent(
                Subject: "Confirm your new Ask Lucy email",
                PreheaderText: "Confirm this address to finish changing your Ask Lucy account email.",
                Heading: "Confirm your new email address",
                BodyParagraphs: ["You requested to change your Ask Lucy account email to this address."],
                SafetyNote: "If you didn't request this, you can safely ignore this message.",
                PrimaryAction: new EmailAction("Confirm email change", "https://asklucy.io/confirm-email-change?userId=user-1&newEmail=new%40example.com&token=tok")),
            new AccountEmailContent(
                Subject: "Reset your Ask Lucy password",
                PreheaderText: "Use this link to choose a new Ask Lucy password.",
                Heading: "Reset your password",
                BodyParagraphs: ["Hi user@example.com,", "We received a request to reset your Ask Lucy password."],
                SafetyNote: "This link expires in one hour and can be used once.",
                PrimaryAction: new EmailAction("Reset my password", "https://asklucy.io/reset-password?userId=user-1&token=tok")),
            new AccountEmailContent(
                Subject: "Confirm your Ask Lucy account",
                PreheaderText: "Here is a fresh link to confirm your Ask Lucy account.",
                Heading: "Confirm your account",
                BodyParagraphs: ["Hi user@example.com,", "Here is a fresh link to confirm your Ask Lucy account."],
                SafetyNote: "If you did not ask for this, you can ignore this email.",
                PrimaryAction: new EmailAction("Confirm my email", "https://asklucy.io/confirm-email?userId=user-1&token=tok")),
        };

    [Fact]
    public void Render_ShouldIncludeTheBrandWordmarkAndSharedFooter_InBothBodies()
    {
        var (htmlBody, textBody) = _renderer.Render(ContentWithAction());

        htmlBody.Should().Contain("Ask Lucy");
        htmlBody.Should().Contain("This is an automated message, please do not reply to this email.");
        textBody.Should().Contain("Ask Lucy");
        textBody.Should().Contain("This is an automated message, please do not reply to this email.");
    }

    [Fact]
    public void Render_ShouldCarryHeadingParagraphsAndSafetyNote_InBothBodies()
    {
        var content = ContentWithAction();
        var (htmlBody, textBody) = _renderer.Render(content);

        htmlBody.Should().Contain(content.Heading);
        textBody.Should().Contain(content.Heading);

        foreach (var paragraph in content.BodyParagraphs)
        {
            htmlBody.Should().Contain(paragraph);
            textBody.Should().Contain(paragraph);
        }

        htmlBody.Should().Contain(content.SafetyNote);
        textBody.Should().Contain(content.SafetyNote);
    }

    [Fact]
    public void Render_ShouldIncludeThePrimaryActionLabelAndUrl_WhenSet()
    {
        var content = ContentWithAction();
        var (htmlBody, textBody) = _renderer.Render(content);

        htmlBody.Should().Contain(content.PrimaryAction!.Label);
        htmlBody.Should().Contain(System.Net.WebUtility.HtmlEncode(content.PrimaryAction.Url));
        textBody.Should().Contain(content.PrimaryAction.Label);
        textBody.Should().Contain(content.PrimaryAction.Url);
    }

    [Fact]
    public void Render_ShouldOmitActionMarkup_WhenPrimaryActionIsNull()
    {
        var (htmlBody, _) = _renderer.Render(ContentWithoutAction());

        htmlBody.Should().NotContain("class=\"al-cta-cell\"");
    }

    [Fact]
    public void Render_ShouldEmitDarkModeMetaTagsAndMediaQuery()
    {
        var (htmlBody, _) = _renderer.Render(ContentWithAction());

        htmlBody.Should().Contain("<meta name=\"color-scheme\" content=\"light dark\">");
        htmlBody.Should().Contain("<meta name=\"supported-color-schemes\" content=\"light dark\">");
        htmlBody.Should().Contain("@media (prefers-color-scheme: dark)");
        htmlBody.Should().MatchRegex(@"@media \(prefers-color-scheme: dark\)[\s\S]*al-bg[\s\S]*al-heading[\s\S]*al-cta-cell[\s\S]*\}");
    }

    [Theory]
    [MemberData(nameof(AllContentVariants))]
    public void Render_ShouldEmitAtMostOneCtaElement_AndLabelledWithTheActionText(AccountEmailContent content)
    {
        var (htmlBody, _) = _renderer.Render(content);

        var ctaCount = CountOccurrences(htmlBody, "class=\"al-cta-cell\"");

        if (content.PrimaryAction is { } action)
        {
            ctaCount.Should().Be(1);
            htmlBody.Should().Contain($">{System.Net.WebUtility.HtmlEncode(action.Label)}</a>");
        }
        else
        {
            ctaCount.Should().Be(0);
        }

        htmlBody.Should().Contain(content.SafetyNote);
    }

    [Theory]
    [MemberData(nameof(AllContentVariants))]
    public void Render_ShouldEmitExactlyOneVisibleLogoImage_AndNoTrackingPixelOrUnexpectedLinks(AccountEmailContent content)
    {
        var (htmlBody, _) = _renderer.Render(content);

        // Exactly the one visible, non-1x1, alt-labelled brand logo — never a hidden tracking pixel.
        CountOccurrences(htmlBody, "<img").Should().Be(1);
        htmlBody.Should().Contain("src=\"https://asklucy.io/brandmark.png\"");
        htmlBody.Should().NotContain("width=\"1\" height=\"1\"");

        var hrefs = System.Text.RegularExpressions.Regex.Matches(htmlBody, "href=\"([^\"]*)\"")
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));

        foreach (var href in hrefs)
        {
            href.Should().Be(content.PrimaryAction!.Url);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
