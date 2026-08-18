namespace AskLucy.Web.Contracts;

/// <summary>contracts/projects-api.md — `POST /api/v1/projects`.</summary>
public sealed record CreateProjectRequest(string Name);

/// <summary>contracts/projects-api.md — `PUT /api/v1/projects/{id}`.</summary>
public sealed record RenameProjectRequest(string Name);

/// <summary>contracts/projects-api.md — `PUT /api/v1/chats/{chatId}/project`. Pass `projectId: null` to remove the conversation from its Project.</summary>
public sealed record AssignProjectRequest(Guid? ProjectId);
