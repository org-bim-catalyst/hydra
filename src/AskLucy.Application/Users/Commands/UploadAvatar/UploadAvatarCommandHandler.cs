using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.UploadAvatar;

/// <summary>Replaces the legacy inline BLOB with file storage + a signed URL (FR-025).</summary>
public sealed class UploadAvatarCommandHandler(
    IFileStorage fileStorage,
    IUserProfileRepository profiles,
    ICurrentUserAccessor currentUser) : IRequestHandler<UploadAvatarCommand, string>
{
    public async Task<string> Handle(UploadAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var storedFileName = await fileStorage.SaveAsync(request.Content, request.FileNameHint, cancellationToken);
        await profiles.SetAvatarFileNameAsync(userId, storedFileName, cancellationToken);

        return storedFileName;
    }
}
