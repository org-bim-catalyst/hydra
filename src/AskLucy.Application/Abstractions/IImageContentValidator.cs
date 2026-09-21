namespace AskLucy.Application.Abstractions;

/// <summary>
/// Content-based (magic-byte) image validation, independent of any caller-supplied file
/// extension or <c>Content-Type</c> header — both are attacker-controlled and MUST NOT be
/// trusted alone (constitution §8). Shared by <c>ExternalProfilePictureSyncJob</c> and
/// <c>UploadAvatarCommandHandler</c> so avatar bytes are validated identically regardless of
/// whether they arrived via manual upload or OAuth-provider sync.
/// </summary>
public interface IImageContentValidator
{
    /// <summary>
    /// Inspects the leading bytes of <paramref name="content"/> for a known image file signature.
    /// Leaves <paramref name="content"/>'s position unchanged from where it started (rewinds
    /// after reading), so the caller can still pass it on to storage afterward.
    /// </summary>
    bool IsValidImage(Stream content, out string? detectedContentType);
}
