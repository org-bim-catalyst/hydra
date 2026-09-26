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

/// <summary>
/// specs/068 US2, contracts/retry-api.md — asking for a previously failed action to be run again.
///
/// <para>
/// <b>A message id and nothing else.</b> The capability, its arguments and its target all come from
/// what the server itself recorded on that turn; accepting them from the client would make this a
/// general-purpose capability-invocation endpoint wearing a retry's clothes (FR-010, constitution
/// §5). Mutually exclusive with <see cref="ChatRequest.SelectedAction"/>.
/// </para>
/// </summary>
public sealed record RetryRequest(Guid FailedMessageId);

public sealed record ChatRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters = null,
    SelectedActionRequest? SelectedAction = null,
    RetryRequest? Retry = null);

public sealed record TranslateRequest(Guid ChatId, string Text, string TargetLanguage);

public sealed record GenerateImageRequest(Guid ChatId, string Prompt);

/// <summary>
/// The generated image is stored as the caller's own document; the client fetches it through
/// <c>GET /documents/{DocumentId}/download</c> (a fresh signed URL), never a provider address.
/// </summary>
public sealed record GenerateImageResponse(Guid DocumentId);

public sealed record TranscriptionResponse(string Text);

public sealed record UpdateAiProviderRequest(bool? IsEnabled, Guid? DefaultModelId, bool? ClearDefaultModel);

/// <summary>specs/055-role-management contracts §4 — the permission-split successor to <c>UpdateAiProviderRequest</c>'s deprecated default-model fields. <c>DefaultModelId</c> null clears the default.</summary>
public sealed record SetProviderDefaultModelRequest(Guid? DefaultModelId);

/// <summary>
/// Null <c>ProviderId</c> clears the assignment, returning the capability to the platform default.
/// <c>ModelId</c> pins one of that provider's models (required for <c>ImageGeneration</c>); null
/// follows the provider's own default model.
/// </summary>
public sealed record SetAiCapabilityAssignmentRequest(Guid? ProviderId, Guid? ModelId = null);

public sealed record SetAiProviderCredentialRequest(string ApiKey);

/// <summary>specs/077 — setting key to its new value, as text ("true"/"false" for a switch); keys left out keep their value.</summary>
public sealed record UpdateAiCapabilitySettingsRequest(IReadOnlyDictionary<string, string> Values);


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

/// <summary>specs/070 contracts/admin-voice.md — adds one of the platform's voice engines. <c>ApiKey</c> is required only by engines that need one.</summary>
public sealed record AddVoiceProviderRequest(string ProviderKey, string? ApiKey);

/// <summary>specs/070 — makes a voice provider Lucy's primary voice, speaking with <c>VoiceId</c>.</summary>
public sealed record SetPrimaryVoiceProviderRequest(Guid ProviderId, string VoiceId);

/// <summary>specs/070 — speaks <c>Text</c> in <c>Language</c> with one of the provider's voices, for the administrator to audition.</summary>
public sealed record PreviewVoiceRequest(string VoiceId, string Text, string Language);
