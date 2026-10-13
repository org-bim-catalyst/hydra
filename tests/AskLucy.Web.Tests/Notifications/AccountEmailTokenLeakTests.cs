using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.ChangeEmail;
using AskLucy.Application.Authentication.Commands.Register;
using AskLucy.Application.Authentication.Commands.RequestAccountSupport;
using AskLucy.Application.Authentication.Commands.RequestPasswordReset;
using AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;
using AskLucy.Application.Authentication.Commands.ResetPassword;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// T122 — specs/067 US9-B, FR-009d and SC-007. Every account email carries a one-time link, and that link is
/// minted when the email is sent and used only to render it. After each type has been sent for real, through
/// the real handlers, outbox, workers and database, no token and no link may appear in <c>Notifications</c>,
/// <c>NotificationDeliveries</c>, <c>NotificationOutboxEvents</c>, <c>NotificationAuditLogs</c> or a captured
/// log line. The tokens come from the emails the fake SMTP server received, so the scan looks for exactly
/// what a reader of the inbox would hold.
/// </summary>
public sealed partial class AccountEmailTokenLeakTests(AccountEmailHubFactory factory) : IClassFixture<AccountEmailHubFactory>
{
    private static readonly JsonSerializerOptions ScanJson = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles, WriteIndented = false };

    [GeneratedRegex(@"[?&]token=(?<token>[^&\s""<]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenParameter();

    [GeneratedRegex(@"https?://[^\s""<]+", RegexOptions.CultureInvariant)]
    private static partial Regex Url();

    [Fact]
    public async Task EveryAccountEmailType_LeavesNoTokenOrLinkInAnyStorageOrLogSink()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..10];
        var registered = $"leak-reg-{run}@example.com";
        var existing = $"leak-existing-{run}@example.com";
        var changeTo = $"leak-new-{run}@example.com";
        var userIds = new List<string>();
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // 1. Registration: the confirmation email.
            var registration = await mediator.Send(new RegisterCommand(registered, "Leak-Test-Passw0rd!", "Lea", "Kage"), ct);
            registration.Outcome.Should().Be(AuthOutcome.Success);
            userIds.Add(registration.UserId!);
            var confirmation = await WaitForEmailAsync(registered, "Confirm your Ask Lucy account", ct);

            // 2. A resend for the still-unconfirmed account.
            await mediator.Send(new ResendEmailConfirmationCommand(registered), ct);
            var resend = await WaitForEmailAsync(registered, "Confirm your Ask Lucy account", ct, skip: 1);

            // 3. A confirmed account asks for a reset, and then uses the link: a password-changed notice follows.
            var existingId = await SeedConfirmedUserAsync(existing);
            userIds.Add(existingId);
            await mediator.Send(new RequestPasswordResetCommand(existing, "203.0.113.9"), ct);
            var reset = await WaitForEmailAsync(existing, "Reset your Ask Lucy password", ct);
            var resetToken = TokenIn(reset)!;
            (await mediator.Send(new ResetPasswordCommand(existingId, resetToken, "Leak-New-Passw0rd!"), ct)).Outcome.Should().Be(PasswordResetOutcome.Success);
            var changed = await WaitForEmailAsync(existing, "Your Ask Lucy password was changed", ct);

            // 4. An email change: the link goes to the new address.
            await mediator.Send(new RequestEmailChangeCommand(existingId, changeTo), ct);
            var change = await WaitForEmailAsync(changeTo, "Confirm your new Ask Lucy email", ct);

            // 5. A locked-out user's message to support.
            await mediator.Send(new RequestAccountSupportCommand(existing, "Please unlock my account.", "203.0.113.9"), ct);
            var support = await WaitForEmailAsync("support@", "Account access request", ct, toPrefix: true);

            // What the readers of those inboxes would hold.
            var emails = new[] { confirmation, resend, reset, changed, change, support };
            var tokens = emails.Select(TokenIn).Where(t => t is not null).Select(t => t!).ToList();
            var links = emails.SelectMany(e => Url().Matches(e.TextBody).Select(m => m.Value)).Where(u => u.Contains("token=", StringComparison.Ordinal)).ToList();
            tokens.Should().HaveCount(4, "confirmation, resend, reset and change each carry one link");
            links.Should().HaveCount(4);
            changed.TextBody.Should().NotContain("http", "a password-changed notice carries no link at all");

            await AssertNoSecretInAnySinkAsync(tokens, links, ct);
        }
        finally
        {
            await CleanupAsync(userIds, [registered, existing, changeTo]);
        }
    }

    private async Task AssertNoSecretInAnySinkAsync(IReadOnlyList<string> tokens, IReadOnlyList<string> links, CancellationToken ct)
    {
        // Every spelling the secret could take in storage: as sent, decoded, and URL-encoded.
        var needles = tokens.SelectMany(t => new[] { t, Uri.UnescapeDataString(t), Uri.EscapeDataString(Uri.UnescapeDataString(t)), HttpUtility.UrlEncode(Uri.UnescapeDataString(t)) })
            .Concat(links).Where(n => n.Length >= 12).Distinct(StringComparer.Ordinal).ToList();
        needles.Should().NotBeEmpty();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

        var sinks = new Dictionary<string, List<string>>
        {
            ["Notifications"] = (await db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct)).Select(r => JsonSerializer.Serialize(r, ScanJson)).ToList(),
            ["NotificationDeliveries"] = (await db.Set<AskLucy.Domain.Notifications.NotificationDelivery>().AsNoTracking().ToListAsync(ct)).Select(r => JsonSerializer.Serialize(r, ScanJson)).ToList(),
            ["NotificationOutboxEvents"] = (await db.NotificationOutboxEvents.AsNoTracking().ToListAsync(ct)).Select(r => JsonSerializer.Serialize(r, ScanJson)).ToList(),
            ["NotificationAuditLogs"] = (await db.NotificationAuditLogs.AsNoTracking().ToListAsync(ct)).Select(r => JsonSerializer.Serialize(r, ScanJson)).ToList(),
            ["captured log"] = [.. CapturedLogSink.Shared.Lines],
        };

        foreach (var (sink, rows) in sinks)
        {
            foreach (var needle in needles)
            {
                rows.Where(row => row.Contains(needle, StringComparison.Ordinal)).Should().BeEmpty($"no token or link may be stored in or written to the {sink}");
            }
        }

        // The scan is only meaningful if it was looking at real rows.
        sinks["Notifications"].Should().NotBeEmpty();
        sinks["NotificationDeliveries"].Should().NotBeEmpty();
        sinks["NotificationOutboxEvents"].Should().NotBeEmpty();
    }

    private static string? TokenIn(AskLucy.Application.Abstractions.EmailMessage message)
    {
        var match = TokenParameter().Match(message.TextBody);
        return match.Success ? match.Groups["token"].Value : null;
    }

    private static async Task<AskLucy.Application.Abstractions.EmailMessage> WaitForEmailAsync(
        string to, string subject, CancellationToken ct, int skip = 0, bool toPrefix = false)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var matches = ScriptableEmailSender.Shared.AcceptedMessages
                .Where(m => (toPrefix ? m.To.StartsWith(to, StringComparison.OrdinalIgnoreCase) : string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)) && m.Subject == subject)
                .ToList();
            if (matches.Count > skip)
            {
                return matches[skip];
            }

            await Task.Delay(250, ct);
        }

        throw new TimeoutException($"No '{subject}' email reached {to} within 90 seconds.");
    }

    private async Task<string> SeedConfirmedUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user, "Seed-Password-1!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task CleanupAsync(IReadOnlyList<string> userIds, IReadOnlyList<string> addresses)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && userIds.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        foreach (var address in addresses)
        {
            await db.NotificationOutboxEvents.Where(e => e.RecipientJson.Contains(address)).ExecuteDeleteAsync();
        }

        await db.PasswordResetTokens.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        foreach (var id in userIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }
}
