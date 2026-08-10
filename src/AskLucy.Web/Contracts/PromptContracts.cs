using AskLucy.Application.Ai;
using AskLucy.Application.Prompts;
using AskLucy.Domain.Prompts;

namespace AskLucy.Web.Contracts;

public sealed record CreatePromptRequest(
    string Name,
    string? Description,
    PromptType PromptType,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    Guid? CategoryId,
    Guid? FolderId,
    PromptCapabilityRequirements? RequiredCapabilities,
    string? PreferredModelKey,
    IReadOnlyList<PromptVariableDto>? Variables);

public sealed record UpdatePromptRequest(
    string Name,
    string? Description,
    PromptType PromptType,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    Guid? CategoryId,
    Guid? FolderId,
    PromptCapabilityRequirements? RequiredCapabilities,
    string? PreferredModelKey,
    IReadOnlyList<PromptVariableDto>? Variables,
    string? ChangeDescription);

public sealed record PreviewPromptRequest(IReadOnlyDictionary<string, string?>? VariableValues);

public sealed record ExecutePromptRequest(
    int? VersionNumber,
    IReadOnlyDictionary<string, string?>? VariableValues,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    bool UseRagContext,
    IReadOnlyList<Guid>? KnowledgeBaseIds,
    bool UseMemoryContext);

public sealed record SaveTestCaseRequest(
    string Name,
    string VariableValuesJson,
    string? ExpectedOutput,
    string? EvaluationCriteria,
    string ProviderKey,
    string ModelKey,
    Guid? SourceExecutionId);

public sealed record RateExecutionRequest(PromptRatingValue Value);

public sealed record SetFavoriteRequest(bool IsFavorite);

public sealed record SetPinnedRequest(bool IsPinned);

public sealed record AddPromptTagRequest(string Value);

public sealed record CreatePromptCategoryRequest(string Name);

public sealed record CreatePromptFolderRequest(string Name, Guid? ParentFolderId);

public sealed record RenamePromptFolderRequest(string Name);

public sealed record MovePromptFolderRequest(Guid? NewParentFolderId);

public sealed record ExportPromptsRequest(IReadOnlyList<Guid> PromptIds);
