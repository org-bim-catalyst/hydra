namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background sync of an OAuth provider's profile-picture claim into the user's avatar
/// (specs/062-external-login-profile-sync). Enqueued via <c>IBackgroundJobClient</c> against
/// this interface, never the concrete type, mirroring <see cref="IPasswordEmailJob"/>.
/// <para>
/// Dispatch is deliberately off the sign-in/link request path: the picture URL comes from an
/// untrusted, provider-supplied claim, and fetching it synchronously would let a slow or
/// unreachable host add latency to, or fail, an otherwise-successful sign-in (constitution §8).
/// </para>
/// </summary>
public interface IExternalProfilePictureSyncJob
{
    /// <summary>
    /// Fetches <paramref name="pictureUrl"/> and stores it as the user's avatar, mirroring
    /// <c>UploadAvatarCommandHandler</c>'s storage path. A failure here (invalid host, fetch
    /// error, oversize, storage error, or failed <see cref="IImageContentValidator"/> check)
    /// MUST be logged and MUST NOT surface to the user, and MUST leave the previously stored
    /// avatar (if any) untouched.
    /// </summary>
    Task SyncAsync(string userId, string pictureUrl, CancellationToken cancellationToken = default);
}
