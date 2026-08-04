using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteCategory;

/// <summary>Deletes a custom category the caller owns; every knowledge base referencing it falls back to Uncategorized (FR-021).</summary>
public sealed record DeleteCategoryCommand(Guid Id) : IRequest;
