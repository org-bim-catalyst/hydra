using MediatR;

namespace AskLucy.Application.Documents.Commands.RetryProcessing;

public sealed record RetryProcessingCommand(Guid DocumentId) : IRequest;
