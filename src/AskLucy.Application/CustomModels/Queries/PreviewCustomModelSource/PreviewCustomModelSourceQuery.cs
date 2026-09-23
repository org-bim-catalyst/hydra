using MediatR;

namespace AskLucy.Application.CustomModels.Queries.PreviewCustomModelSource;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>POST source-preview</c>. Parses the URL and checks
/// whether the derived name is free. Makes <b>no outbound call</b>.
/// </summary>
public sealed record PreviewCustomModelSourceQuery(string? Source) : IRequest<SourcePreviewDto>;
