using MediatR;

namespace AskLucy.Application.Users.Commands.UploadAvatar;

public sealed record UploadAvatarCommand(Stream Content, string FileNameHint) : IRequest<string>;
