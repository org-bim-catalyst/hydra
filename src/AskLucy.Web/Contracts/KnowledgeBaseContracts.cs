namespace AskLucy.Web.Contracts;

/// <summary>contracts/knowledge-bases-api.md `POST /api/v1/knowledge-bases`.</summary>
public sealed record CreateKnowledgeBaseRequest(
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags);

/// <summary>contracts/knowledge-bases-api.md `PATCH /api/v1/knowledge-bases/{id}` — full-replace update (see <c>UpdateKnowledgeBaseDetailsCommand</c>'s doc comment).</summary>
public sealed record UpdateKnowledgeBaseDetailsRequest(
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    string? Notes);

/// <summary>contracts/knowledge-base-folders-documents-api.md `POST .../folders`.</summary>
public sealed record CreateFolderRequest(string Name, Guid? ParentFolderId);

public sealed record RenameFolderRequest(string Name);

public sealed record MoveFolderRequest(Guid? NewParentFolderId);

/// <summary>`DELETE .../folders/{folderId}` — defaults to `false`, matching the contract's "confirm required only if non-empty" rule.</summary>
public sealed record DeleteFolderRequest(bool Confirm = false);

public sealed record MoveDocumentRequest(Guid? NewFolderId);

/// <summary>contracts/knowledge-base-taxonomy-api.md `POST /api/v1/knowledge-bases/categories`.</summary>
public sealed record CreateCustomCategoryRequest(string Name);
