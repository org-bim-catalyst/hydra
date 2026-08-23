namespace AskLucy.Infrastructure.TheDigitalCore;

/// <summary>Thrown when a TheDigitalCore API call fails after retries (ADR-0009). Never silently swallowed by any caller (constitution &#167;2.VIII).</summary>
public sealed class TheDigitalCoreIntegrationException(string message, Exception? innerException = null) : Exception(message, innerException);
