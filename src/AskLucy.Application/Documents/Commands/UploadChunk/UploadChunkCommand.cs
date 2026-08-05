using MediatR;

namespace AskLucy.Application.Documents.Commands.UploadChunk;

public sealed record UploadChunkCommand(Guid UploadSessionId, int ChunkIndex, Stream ChunkContent) : IRequest<UploadChunkResultDto>;

public sealed record UploadChunkResultDto(int ReceivedChunkIndex, int NextExpectedChunkIndex);
