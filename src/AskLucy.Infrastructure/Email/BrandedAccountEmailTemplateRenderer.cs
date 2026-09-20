using System.Globalization;
using System.Net;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Renders every account email through one Flumeria-branded, table-based HTML shell that is
/// legible in both light and dark mode across Gmail, Apple Mail, and Outlook (research.md Topic
/// 2), plus a plain-text alternative derived from the same content so the two bodies can never
/// drift apart (research.md Topic 4). Purely presentational beyond the single visible brand logo
/// in the header — no tracking pixel, click-redirect, or analytics mechanism is ever emitted
/// (FR-010).
/// </summary>
public sealed class BrandedAccountEmailTemplateRenderer(IOptions<AppOptions> appOptions) : IEmailTemplateRenderer
{
    // Light-mode palette — Flumeria auth-flow tokens (research.md Topic 3 / flumeriaPalette.ts).
    private const string LightBackground = "#FAFAF8";
    private const string LightCardBackground = "#FFFFFF";
    private const string LightHeading = "#171717";
    private const string LightBody = "#4B5563";
    private const string LightBorder = "#E5E7EB";
    private const string BrandGreen = "#15803D";
    private const string BrandGreenDark = "#116932";
    private const string OnBrandGreen = "#FFFFFF";

    // Dark-mode derivative — no in-app equivalent exists (auth pages are light-only), so these
    // values are hand-picked and AA-contrast-checked for email specifically (research.md Topic 3).
    private const string DarkBackground = "#0A0A0A";
    private const string DarkCardBackground = "#14130F";
    private const string DarkHeading = "#FAFAF8";
    private const string DarkBody = "#D4D4D8";
    private const string DarkBorder = "#27272A";

    public (string HtmlBody, string TextBody) Render(AccountEmailContent content) =>
        (RenderHtml(content), RenderText(content));

    private string RenderHtml(AccountEmailContent content)
    {
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        sb.Append("<meta name=\"color-scheme\" content=\"light dark\">\n");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light dark\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<title>{Encode(content.Subject)}</title>\n");
        sb.Append("<style>\n");
        sb.Append("  body,table,td,a { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }\n");
        sb.Append("  table,td { mso-table-lspace:0pt; mso-table-rspace:0pt; }\n");
        sb.Append("  img { -ms-interpolation-mode:bicubic; border:0; }\n");
        sb.Append("  body { margin:0; padding:0; width:100% !important; }\n");
        sb.Append("  .al-container { max-width:600px; }\n");
        sb.Append("  @media (prefers-color-scheme: dark) {\n");
        sb.Append($"    .al-bg {{ background-color:{DarkBackground} !important; }}\n");
        sb.Append($"    .al-card {{ background-color:{DarkCardBackground} !important; border-color:{DarkBorder} !important; }}\n");
        sb.Append($"    .al-heading {{ color:{DarkHeading} !important; }}\n");
        sb.Append($"    .al-body {{ color:{DarkBody} !important; }}\n");
        sb.Append($"    .al-footer {{ color:{DarkBody} !important; }}\n");
        sb.Append($"    .al-cta-cell {{ background-color:{BrandGreenDark} !important; }}\n");
        sb.Append("  }\n");
        sb.Append("  @media screen and (max-width: 600px) {\n");
        sb.Append("    .al-container { width:100% !important; }\n");
        sb.Append("    .al-padded { padding-left:24px !important; padding-right:24px !important; }\n");
        sb.Append("  }\n");
        sb.Append("</style>\n");
        sb.Append("</head>\n");

        // The preheader is hidden but read by inbox list previews; kept out of the visible body.
        sb.Append($"<body class=\"al-bg\" style=\"margin:0; padding:0; background-color:{LightBackground};\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<div style=\"display:none; max-height:0; overflow:hidden; opacity:0;\">{Encode(content.PreheaderText)}</div>\n");

        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" class=\"al-bg\" style=\"background-color:{LightBackground};\">\n");
        sb.Append("<tr><td align=\"center\" style=\"padding:32px 16px;\">\n");
        sb.Append($"<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" class=\"al-container al-card\" style=\"max-width:600px; width:100%; background-color:{LightCardBackground}; border:1px solid {LightBorder}; border-radius:8px;\">\n");

        // Header — brand logo (hosted at the frontend's own static root, never a third-party or
        // tracking-analytics domain) plus the wordmark, in a table cell so it lays out identically
        // in clients that block remote images (the alt text still reads "Ask Lucy").
        var logoUrl = $"{appOptions.Value.FrontendBaseUrl}/lucy-portrait.png";
        sb.Append("<tr><td class=\"al-padded\" style=\"padding:32px 32px 24px 32px; text-align:left;\">\n");
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr>\n");
        sb.Append(CultureInfo.InvariantCulture, $"<td style=\"padding-right:8px;\"><img src=\"{Encode(logoUrl)}\" width=\"28\" height=\"28\" alt=\"\" style=\"display:block; border:0; width:28px; height:28px; border-radius:50%;\"></td>\n");
        sb.Append(CultureInfo.InvariantCulture, $"<td><span style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:20px; font-weight:700; color:{BrandGreen};\">Ask Lucy</span></td>\n");
        sb.Append("</tr></table>\n");
        sb.Append("</td></tr>\n");

        // Body.
        sb.Append("<tr><td class=\"al-padded\" style=\"padding:0 32px 8px 32px;\">\n");
        // Heading/Greeting/BodyParagraphs/SafetyNote/FooterNote are pre-sanitized HTML content
        // per contracts/email-template-renderer.md — any user-controlled substring was already
        // HTML-encoded by the caller, so re-encoding here would corrupt entities (double-escape).
        sb.Append(CultureInfo.InvariantCulture, $"<h1 class=\"al-heading\" style=\"margin:0 0 16px 0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:22px; line-height:28px; color:{LightHeading};\">{content.Heading}</h1>\n");

        if (!string.IsNullOrWhiteSpace(content.Greeting))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"al-body\" style=\"margin:0 0 16px 0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:15px; line-height:22px; color:{LightBody};\">{content.Greeting}</p>\n");
        }

        foreach (var paragraph in content.BodyParagraphs)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"al-body\" style=\"margin:0 0 16px 0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:15px; line-height:22px; color:{LightBody}; word-break:break-word;\">{paragraph}</p>\n");
        }

        if (content.PrimaryAction is { } action)
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 24px 0;\"><tr>\n");
            sb.Append(CultureInfo.InvariantCulture, $"<td class=\"al-cta-cell\" style=\"background-color:{BrandGreen}; border-radius:6px;\">\n");
            sb.Append(CultureInfo.InvariantCulture, $"<a href=\"{Encode(action.Url)}\" style=\"display:inline-block; padding:12px 28px; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:15px; font-weight:600; color:{OnBrandGreen}; text-decoration:none;\">{Encode(action.Label)}</a>\n");
            sb.Append("</td>\n</tr></table>\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<p class=\"al-body\" style=\"margin:0 0 16px 0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:13px; line-height:20px; color:{LightBody};\">{content.SafetyNote}</p>\n");
        sb.Append("</td></tr>\n");

        // Footer.
        sb.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"al-padded\" style=\"padding:24px 32px 32px 32px; border-top:1px solid {LightBorder};\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<p class=\"al-footer al-body\" style=\"margin:0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:12px; line-height:18px; color:{LightBody};\">This is an automated message, please do not reply to this email.</p>\n");
        if (!string.IsNullOrWhiteSpace(content.FooterNote))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"al-footer al-body\" style=\"margin:8px 0 0 0; font-family:Segoe UI,Helvetica,Arial,sans-serif; font-size:12px; line-height:18px; color:{LightBody};\">{content.FooterNote}</p>\n");
        }
        sb.Append("</td></tr>\n");

        sb.Append("</table>\n</td></tr>\n</table>\n");
        sb.Append("</body>\n</html>");

        return sb.ToString();
    }

    private static string RenderText(AccountEmailContent content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ask Lucy");
        sb.AppendLine();
        sb.AppendLine(Decode(content.Heading));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(content.Greeting))
        {
            sb.AppendLine(Decode(content.Greeting));
            sb.AppendLine();
        }

        foreach (var paragraph in content.BodyParagraphs)
        {
            sb.AppendLine(Decode(paragraph));
            sb.AppendLine();
        }

        if (content.PrimaryAction is { } action)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{action.Label}: {action.Url}");
            sb.AppendLine();
        }

        sb.AppendLine(Decode(content.SafetyNote));
        sb.AppendLine();
        sb.AppendLine("This is an automated message, please do not reply to this email.");

        if (!string.IsNullOrWhiteSpace(content.FooterNote))
        {
            sb.AppendLine(Decode(content.FooterNote));
        }

        return sb.ToString().TrimEnd();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Decode(string value) => WebUtility.HtmlDecode(value);
}
