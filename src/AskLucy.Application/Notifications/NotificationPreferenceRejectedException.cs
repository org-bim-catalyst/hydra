namespace AskLucy.Application.Notifications;

/// <summary>
/// An update asked to switch off a pair the user can't change (mandatory, FR-032). The request is atomic, so
/// nothing was applied; <see cref="Errors"/> is keyed <c>changes[i]</c> by the offending change's position.
/// Mapped to 422 by ProblemDetailsMiddleware.
/// </summary>
public sealed class NotificationPreferenceRejectedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more notification preferences can't be changed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
