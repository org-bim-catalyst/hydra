using MediatR;

namespace AskLucy.Application.Prompts.Queries.GetFolderTree;

/// <summary>The caller's full folder tree, flat (the client builds the tree from `ParentFolderId`) (spec.md FR-050, FR-054).</summary>
public sealed record GetFolderTreeQuery : IRequest<IReadOnlyList<PromptFolderDto>>;
