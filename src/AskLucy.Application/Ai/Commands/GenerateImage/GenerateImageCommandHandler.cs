using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.GenerateImage;

public sealed class GenerateImageCommandHandler(IAIProvider aiProvider) : IRequestHandler<GenerateImageCommand, Uri>
{
    public Task<Uri> Handle(GenerateImageCommand request, CancellationToken cancellationToken) =>
        aiProvider.GenerateImageAsync(request.Prompt, cancellationToken);
}
