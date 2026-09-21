using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Users.Commands.UploadAvatar;

/// <summary>Replaces the legacy inline BLOB with file storage + a signed URL (FR-025).</summary>
public sealed class UploadAvatarCommandHandler(
    IFileStorage fileStorage,
    IUserProfileRepository profiles,
    ICurrentUserAccessor currentUser,
    IImageContentValidator imageContentValidator) : IRequestHandler<UploadAvatarCommand, string>
{
    public async Task<string> Handle(UploadAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // Content-based validation (constitution §8) — the caller-supplied file name/Content-Type
        // is never trusted alone; only the actual leading bytes decide whether this is a real image.
        if (!imageContentValidator.IsValidImage(request.Content, out _))
        {
            throw new DomainRuleViolationException("The uploaded file is not a recognized image format.");
        }

        var storedFileName = await fileStorage.SaveAsync(request.Content, request.FileNameHint, cancellationToken);
        await profiles.SetAvatarFileNameAsync(userId, storedFileName, cancellationToken);

        return storedFileName;
    }
}
