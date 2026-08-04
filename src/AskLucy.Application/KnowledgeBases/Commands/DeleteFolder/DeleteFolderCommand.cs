using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteFolder;

/// <summary>Soft-deletes a folder — requires <paramref name="Confirm"/> when the folder still contains subfolders or documents (FR-015).</summary>
public sealed record DeleteFolderCommand(Guid KnowledgeBaseId, Guid FolderId, bool Confirm) : IRequest;
