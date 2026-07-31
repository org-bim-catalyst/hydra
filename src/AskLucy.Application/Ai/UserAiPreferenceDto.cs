namespace AskLucy.Application.Ai;

/// <summary>contracts/preferences.md — `isPlatformDefault: true` distinguishes "the fallback" from "your saved choice" (User Story 3, Acceptance Scenario 1).</summary>
public sealed record UserAiPreferenceDto(
    Guid DefaultProviderId,
    Guid DefaultModelId,
    GenerationParametersDto? DefaultGenerationParameters,
    bool IsPlatformDefault);
