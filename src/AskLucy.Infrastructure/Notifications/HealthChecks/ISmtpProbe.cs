namespace AskLucy.Infrastructure.Notifications.HealthChecks;

/// <summary>Checks that the mail host accepts a connection, TLS and the configured login, without sending anything.</summary>
public interface ISmtpProbe
{
    /// <returns>A safe, human-readable summary such as <c>STARTTLS ok</c>.</returns>
    /// <exception cref="Exception">Any failure to connect, secure or authenticate.</exception>
    Task<string> ProbeAsync(CancellationToken cancellationToken);
}
