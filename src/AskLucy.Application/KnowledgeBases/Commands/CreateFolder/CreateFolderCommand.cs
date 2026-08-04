using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateFolder;

/// <summary>Creates a folder inside a knowledge base, optionally nested inside an existing folder (FR-012).</summary>
public sealed record CreateFolderCommand(Guid KnowledgeBaseId, string Name, Guid? ParentFolderId) : IRequest<KnowledgeBaseFolderDto>;
