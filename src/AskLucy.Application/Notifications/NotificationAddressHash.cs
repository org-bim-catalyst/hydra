using System.Security.Cryptography;
using System.Text;

namespace AskLucy.Application.Notifications;

/// <summary>
/// The only form in which an address that may not belong to any account is ever logged (FR-009e): a
/// lowercase hex SHA-256 of the trimmed, lower-cased address. It lets support match a log line to an address
/// they are given, without the log itself being a list of who asked to recover which account.
/// </summary>
public static class NotificationAddressHash
{
    public static string Of(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant())));
    }
}
