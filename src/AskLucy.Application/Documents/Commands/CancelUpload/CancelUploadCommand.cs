using MediatR;

namespace AskLucy.Application.Documents.Commands.CancelUpload;

public sealed record CancelUploadCommand(Guid UploadSessionId) : IRequest;
