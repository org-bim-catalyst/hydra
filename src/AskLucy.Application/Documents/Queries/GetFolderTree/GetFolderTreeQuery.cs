using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Queries.GetFolderTree;

/// <summary>contracts/document-versions-folders-api.md `GET /api/v1/documents/folders/tree` (FR-033).</summary>
public sealed record GetFolderTreeQuery : IRequest<IReadOnlyList<DocumentFolderDto>>;
