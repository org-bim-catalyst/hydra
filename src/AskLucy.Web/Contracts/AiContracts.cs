using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;

namespace AskLucy.Web.Contracts;

/// <summary>
/// specs/045-conversational-agent-runtime US3, contracts/suggested-actions-api.md §1 — an offered
/// row the user selected. <see cref="Kind"/> is the wire form (<c>"flowVariant"</c>,
/// <c>"capability"</c>, <c>"followUp"</c> or <c>"decline"</c>); <see cref="Key"/>/<see cref="Text"/>/
/// <see cref="Arguments"/> identify which row, but are only ever used to <b>find</b> it —
/// <see cref="AskLucy.Web.Controllers.v1.AiController"/> dispatches the grounded row's own values,
/// never these.
/// </summary>
public sealed record SelectedActionRequest(
    Guid OfferedByMessageId,
    string Kind,
    string? Key,
    string? Text,
    JsonElement? Arguments);

public sealed record ChatRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters = null,
    SelectedActionRequest? SelectedAction = null);

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
