using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// The support mailbox is the existing <c>Smtp:FromSupport</c> setting and nothing else (FR-009c): it is
/// read here at send time and handed to the sender, never stored on a delivery or returned by an API.
/// </summary>
public sealed class SmtpSupportMailboxResolver(IOptionsMonitor<SmtpOptions> options) : ISupportMailboxResolver
{
    public string? GetAddress() =>
        string.IsNullOrWhiteSpace(options.CurrentValue.FromSupport) ? null : options.CurrentValue.FromSupport.Trim();
}
