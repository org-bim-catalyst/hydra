using AskLucy.Application.Options;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications.Email;

/// <summary>
/// The email send limiter (research R6): a token bucket that holds <c>Notifications:Email:MaxPerMinute</c>
/// sends per minute, split into a general lane and a reserved lane. Mandatory mail (a password reset, a
/// security notice) draws from the reserved lane first and then from whatever the general lane has left;
/// optional mail draws only from the general lane. A bulk announcement can therefore drain the general
/// lane without ever delaying an account email (SC-014). A singleton: it is the one view of the shared
/// mail account's allowance in this process.
/// </summary>
public sealed class EmailSendRateLimiter(IOptionsMonitor<NotificationsOptions> options, TimeProvider timeProvider)
{
    private readonly Lock _gate = new();
    private Bucket _general = new(0, 0);
    private Bucket _reserved = new(0, 0);
    private (int General, int Reserved)? _sizedFor;

    /// <summary>
    /// Takes one send's worth of capacity. When none is available returns false with the time until the
    /// next token for this kind of mail, so the caller defers instead of failing.
    /// </summary>
    public bool TryAcquire(bool mandatory, out TimeSpan retryAfter)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            Resize(now);
            _general.Refill(now);
            _reserved.Refill(now);

            if (mandatory && _reserved.TryTake())
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }

            if (_general.TryTake())
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }

            retryAfter = mandatory
                ? Min(_reserved.TimeUntilNextToken(), _general.TimeUntilNextToken())
                : _general.TimeUntilNextToken();
            return false;
        }
    }

    /// <summary>Re-reads the options on every call, so a configuration reload changes the limit without a restart.</summary>
    private void Resize(DateTime now)
    {
        var email = options.CurrentValue.Email;
        var total = Math.Max(1, email.MaxPerMinute);

        // At least one general token: an operator who sets the reserved lane at or above the total must
        // not silence every optional email.
        var reserved = Math.Clamp(email.ReservedPerMinuteForMandatory, 0, total - 1);
        var general = total - reserved;

        if (_sizedFor == (general, reserved))
        {
            return;
        }

        _general = new Bucket(general, general, now);
        _reserved = new Bucket(reserved, reserved, now);
        _sizedFor = (general, reserved);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>A bucket that starts full, holds one minute's worth and refills continuously.</summary>
    private struct Bucket
    {
        private readonly double _perMinute;
        private double _tokens;
        private DateTime _lastRefillUtc;

        public Bucket(double perMinute, double tokens, DateTime? now = null)
        {
            _perMinute = perMinute;
            _tokens = tokens;
            _lastRefillUtc = now ?? DateTime.MinValue;
        }

        public void Refill(DateTime now)
        {
            if (_perMinute <= 0)
            {
                return;
            }

            if (_lastRefillUtc != DateTime.MinValue && now > _lastRefillUtc)
            {
                _tokens = Math.Min(_perMinute, _tokens + (now - _lastRefillUtc).TotalMinutes * _perMinute);
            }

            _lastRefillUtc = now;
        }

        public bool TryTake()
        {
            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }

        public readonly TimeSpan TimeUntilNextToken()
        {
            if (_perMinute <= 0)
            {
                // An empty lane never refills; report a long wait so the caller doesn't spin on it.
                return TimeSpan.FromMinutes(1);
            }

            var missing = 1 - _tokens;
            return missing <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(missing / _perMinute);
        }
    }
}
