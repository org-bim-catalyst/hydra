namespace AskLucy.Domain.Common;

/// <summary>
/// Thrown when an operation would violate a domain invariant. Never a generic
/// <see cref="Exception"/>, per constitution &#167;4 (Error handling).
/// </summary>
public sealed class DomainRuleViolationException(string message) : Exception(message);
