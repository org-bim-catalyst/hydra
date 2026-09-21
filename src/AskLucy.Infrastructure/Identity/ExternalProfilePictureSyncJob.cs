using AskLucy.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Identity;

/// <summary>
/// Fetches an OAuth provider's profile-picture claim URL and stores it as the user's avatar,
/// off the sign-in/link request path (specs/062-external-login-profile-sync). Any failure is
/// logged and swallowed here — by the time this job runs, sign-in has already completed, so
/// there is no user-visible outcome left to surface it to (constitution §2.VIII, spec edge case).
/// </summary>
[AutomaticRetry(Attempts = 3)]
public sealed partial class ExternalProfilePictureSyncJob(
    [FromKeyedServices("ExternalProfilePictureDownload")] IRemoteFileDownloader remoteFileDownloader,
    IImageContentValidator imageContentValidator,
    IFileStorage fileStorage,
    IUserProfileRepository userProfileRepository,
    ILogger<ExternalProfilePictureSyncJob> logger) : IExternalProfilePictureSyncJob
{
    private const long MaxContentLength = 5 * 1024 * 1024;

    private static readonly string[] AllowedHostSuffixes =
    [
        ".googleusercontent.com",
        ".fbsbx.com",
        ".fbcdn.net"
    ];

    public async Task SyncAsync(string userId, string pictureUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri) || !IsAllowedHost(uri))
        {
            LogRejectedHost(logger, userId, pictureUrl);
            return;
        }

        try
        {
            var downloaded = await remoteFileDownloader.DownloadAsync(uri, MaxContentLength, cancellationToken);
            await using var content = downloaded.Content;

            if (!imageContentValidator.IsValidImage(content, out _))
            {
                LogInvalidImageContent(logger, userId);
                return;
            }

            var storedFileName = await fileStorage.SaveAsync(content, "external-profile-picture", cancellationToken);
            await userProfileRepository.SetAvatarFileNameAsync(userId, storedFileName, cancellationToken);
            LogSynced(logger, userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFetchFailed(logger, ex, userId);
        }
    }

    private static bool IsAllowedHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        AllowedHostSuffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    [LoggerMessage(EventId = 5850, Level = LogLevel.Warning, Message = "Rejected external profile picture URL for an untrusted host. UserId={UserId}, PictureUrl={PictureUrl}")]
    private static partial void LogRejectedHost(ILogger logger, string userId, string pictureUrl);

    [LoggerMessage(EventId = 5851, Level = LogLevel.Warning, Message = "Downloaded external profile picture failed content validation and was discarded. UserId={UserId}")]
    private static partial void LogInvalidImageContent(ILogger logger, string userId);

    [LoggerMessage(EventId = 5852, Level = LogLevel.Warning, Message = "Failed to sync external profile picture; the previous avatar (if any) was left unchanged. UserId={UserId}")]
    private static partial void LogFetchFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(EventId = 5853, Level = LogLevel.Information, Message = "External profile picture synced. UserId={UserId}")]
    private static partial void LogSynced(ILogger logger, string userId);
}
