using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases;

public sealed record KnowledgeBaseDocumentDto(
    Guid Id,
    Guid KnowledgeBaseId,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? PageCount,
    KnowledgeBaseDocumentProcessingStatus ProcessingStatus,
    DateTime UploadedAtUtc)
{
    public static KnowledgeBaseDocumentDto FromEntity(KnowledgeBaseDocument document) => new(
        document.Id,
        document.KnowledgeBaseId,
        document.FolderId,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.PageCount,
        document.ProcessingStatus,
        document.UploadedAtUtc);
}
