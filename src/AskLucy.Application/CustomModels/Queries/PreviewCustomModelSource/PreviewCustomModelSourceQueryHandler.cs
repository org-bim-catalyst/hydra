using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using MediatR;

namespace AskLucy.Application.CustomModels.Queries.PreviewCustomModelSource;

public sealed class PreviewCustomModelSourceQueryHandler(ICustomModelRepository customModels)
    : IRequestHandler<PreviewCustomModelSourceQuery, SourcePreviewDto>
{
    public async Task<SourcePreviewDto> Handle(PreviewCustomModelSourceQuery request, CancellationToken cancellationToken)
    {
        if (!HuggingFaceModelSource.TryParse(request.Source, out var source, out var error))
        {
            return new SourcePreviewDto(false, error, null, null, null, null, NameAvailable: false);
        }

        // No derived name means the dialog must ask for one, the same as a taken name.
        var nameAvailable = source.DerivedName is not null
            && !await customModels.NameExistsAsync(source.DerivedName, cancellationToken);

        return new SourcePreviewDto(true, null, source.RepositoryId, source.Revision, source.IgnoredFilePath, source.DerivedName, nameAvailable);
    }
}
