namespace AskLucy.Application.Ai;

/// <summary>
/// Generation parameters a user may configure for a conversation or a single send
/// (FR-014). Every field is optional — an unset field falls back through the inheritance
/// chain (per-send override → conversation default → user default → provider/model
/// default, data-model.md/contracts/chat.md). A model that doesn't support a given field
/// (FR-015) causes the request to be rejected before it reaches a provider, not silently
/// dropped — see <c>GenerationParametersDtoValidator</c>.
/// </summary>
public sealed record GenerationParametersDto(
    double? Temperature = null,
    double? TopP = null,
    int? TopK = null,
    double? PresencePenalty = null,
    double? FrequencyPenalty = null,
    int? MaxTokens = null,
    IReadOnlyList<string>? StopSequences = null,
    long? Seed = null,
    string? ReasoningLevel = null,
    string? ResponseFormat = null,
    bool? JsonMode = null,
    bool? Streaming = null,
    string? SystemPrompt = null,
    string? DeveloperPrompt = null);
