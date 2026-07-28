using System.Collections.Concurrent;
using System.Security.Cryptography;
using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Auth;

/// <summary>
/// Process-memory store — acceptable given spec.md's fewer-than-100-user/low-concurrency,
/// single-instance deployment assumption. Codes are single-use and expire within minutes, so
/// a process restart between issue and consume merely forces the user to retry the sign-in
/// button, not a data-loss or security issue.
/// </summary>
public sealed class InMemoryExternalLoginCodeStore : IExternalLoginCodeStore
{
    private sealed record Entry(string UserId, DateTimeOffset ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public string Issue(string userId, TimeSpan lifetime)
    {
        PruneExpired();

        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _entries[code] = new Entry(userId, DateTimeOffset.UtcNow.Add(lifetime));
        return code;
    }

    public string? TryConsume(string code)
    {
        if (!_entries.TryRemove(code, out var entry))
        {
            return null;
        }

        return entry.ExpiresAtUtc >= DateTimeOffset.UtcNow ? entry.UserId : null;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAtUtc < now)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
