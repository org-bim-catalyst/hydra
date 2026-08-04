using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseFolderTree;

/// <summary>Returns the full folder tree (flat, caller renders the hierarchy from `ParentFolderId`) plus root-level documents — small trees, no pagination (contracts/knowledge-base-folders-documents-api.md).</summary>
public sealed record GetKnowledgeBaseFolderTreeQuery(Guid KnowledgeBaseId) : IRequest<KnowledgeBaseFolderTreeDto>;

public sealed record KnowledgeBaseFolderTreeDto(IReadOnlyList<KnowledgeBaseFolderDto> Folders, IReadOnlyList<KnowledgeBaseDocumentDto> RootDocuments);
