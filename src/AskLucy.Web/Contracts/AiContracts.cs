using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;

namespace AskLucy.Web.Contracts;

public sealed record ChatRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters = null);

public sealed record TranslateRequest(Guid ChatId, string Text, string TargetLanguage);

public sealed record GenerateImageRequest(Guid ChatId, string Prompt);

public sealed record GenerateImageResponse(string Url);

public sealed record TranscriptionResponse(string Text);

public sealed record UpdateAiProviderRequest(bool? IsEnabled, Guid? DefaultModelId, bool? ClearDefaultModel);

/// <summary>Null <c>ProviderId</c> clears the assignment, returning the capability to the platform default.</summary>
public sealed record SetAiCapabilityAssignmentRequest(Guid? ProviderId);

public sealed record SetAiProviderCredentialRequest(string ApiKey);


public sealed record UpdateAiModelStatusRequest(AIModelStatus Status);

public sealed record ApplyProviderModelSyncRequest(IReadOnlyList<ProviderModelInfo> Added, IReadOnlyList<RemovedModelDto> RemovedFromVendor);

public sealed record CreateSpeechToTextSessionRequest(string Language);

public sealed record SaveVoicePreferenceRequest(
    string ConversationMode,
    bool IsMuted,
    string? SelectedVoiceId,
    double? VoiceSpeed,
    double? VoiceStyle,
    string? PreferredMicrophoneDeviceId,
    string? PreferredSpeakerDeviceId,
    string? DefaultLanguage);

public sealed record VoiceReplyRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters,
    string Language);

public sealed record SynthesizeSpeechRequest(string Text, string Language);
