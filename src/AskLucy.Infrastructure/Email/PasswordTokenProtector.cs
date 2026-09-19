using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Mirrors <c>MemoryContentProtector</c>'s use of Data Protection under its own purpose string,
/// applied to a reset token for the seconds it spends as a Hangfire job argument
/// (specs/058-password-recovery research.md Topic 5a).
/// </summary>
public sealed partial class PasswordTokenProtector : IPasswordTokenProtector
{
    private readonly IDataProtector _protector;
    private readonly ILogger<PasswordTokenProtector> _logger;

    public PasswordTokenProtector(IDataProtectionProvider provider, ILogger<PasswordTokenProtector> logger)
    {
        _protector = provider.CreateProtector("AskLucy.PasswordResetTokens");
        _logger = logger;
    }

    public string Protect(string plaintextToken) => _protector.Protect(plaintextToken);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (Exception ex)
        {
            // Normally means the key ring rotated while the job sat queued. Returning null rather
            // than rethrowing lets the caller decide, but it is logged here so the cause is never
            // lost — the caller only sees "could not unprotect".
            LogUnprotectFailed(_logger, ex);
            return null;
        }
    }

    [LoggerMessage(EventId = 5810, Level = LogLevel.Error, Message = "Could not unprotect a queued password reset token; the Data Protection key ring has most likely changed since it was enqueued.")]
    private static partial void LogUnprotectFailed(ILogger logger, Exception exception);
}
