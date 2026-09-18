using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Images;
using AskLucy.Application.Documents.Commands;
using MediatR;

namespace AskLucy.Application.Ai.Commands.GenerateImage;

public sealed class GenerateImageCommandHandler(
    IImageGenerationService imageGenerationService,
    DocumentUploadFinalizer documentUploadFinalizer,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<GenerateImageCommand, GeneratedImageDto>
{
    private const string Actor = "system:image-generation";

    public async Task<GeneratedImageDto> Handle(GenerateImageCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var generated = await imageGenerationService.GenerateAsync(request.Prompt, cancellationToken);

        using var content = new MemoryStream(generated.Image.Content, writable: false);
        var fileName = $"generated-image-{Guid.CreateVersion7()}{generated.Image.FileExtension}";
        var stored = await documentUploadFinalizer.FinalizeAsync(userId, fileName, content, content.Length, Actor, cancellationToken);
        var documentId = stored.IsDuplicate ? stored.DuplicateOfDocumentId!.Value : stored.Document!.Id;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GeneratedImageDto(documentId, generated.ProviderName, generated.ModelKey);
    }
}
