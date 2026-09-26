namespace AskLucy.Application.Authorization;

/// <summary>
/// specs/074 FR-016h–j — an action only a Super User may take. Still an
/// <see cref="UnauthorizedAccessException"/> (a 403, logged as an access denial), but unlike the
/// generic one its message is safe to show and is returned as the Problem Details <c>detail</c>, so
/// the admin UI can say why.
/// </summary>
public sealed class SuperUserRequiredException(string message) : UnauthorizedAccessException(message);
