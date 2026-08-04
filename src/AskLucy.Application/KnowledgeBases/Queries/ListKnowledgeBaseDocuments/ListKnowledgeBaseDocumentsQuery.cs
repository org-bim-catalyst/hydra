using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ListKnowledgeBaseDocuments;

/// <summary>Documents directly inside one folder, or at the knowledge base's root when <paramref name="FolderId"/> is null (contracts/knowledge-base-folders-documents-api.md). Cursor/pageSize accepted for contract-shape stability; not yet paginated in US2 (folder-level document counts are small enough that this is not a functional gap the way the top-level KB search was in US1) — a straightforward `Skip`/`Take` addition if a folder ever needs it.</summary>
public sealed record ListKnowledgeBaseDocumentsQuery(Guid KnowledgeBaseId, Guid? FolderId) : IRequest<PagedResult<KnowledgeBaseDocumentDto>>;
