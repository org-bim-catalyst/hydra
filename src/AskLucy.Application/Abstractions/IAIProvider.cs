using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>Token/latency usage a provider reported for one call (FR-020); fields are null when the provider doesn't report them.</summary>
public sealed record ChatUsage(int? InputTokenCount, int? OutputTokenCount, int? CachedTokenCount, int? ReasoningTokenCount, int? LatencyMs);

public sealed record ChatCompletionResult(string Content, ChatUsage Usage);

/// <summary>One piece of a streamed response. Most chunks carry only <see cref="ContentDelta"/>; a provider's final chunk(s) may additionally carry <see cref="Usage"/> once available (FR-020).</summary>
public sealed record StreamChunk(string? ContentDelta, ChatUsage? Usage = null);

/// <summary>One model a provider currently reports via its own API — research.md Decision 5's "Model Discovery," surfaced to an admin as a diff and never applied to the catalog automatically.</summary>
public sealed record ProviderModelInfo(string ModelKey, string DisplayName, int ContextWindowTokens, int MaxOutputTokens, AIModelCapabilities Capabilities);

/// <summary>
/// Thrown after the single automatic retry (research.md Topic 4 / legacy FR-032) still
/// fails. The WebAPI layer maps this to an <c>ai-provider-unavailable</c> Problem Details
/// response — callers must never see the underlying provider exception.
/// </summary>
public sealed class AiProviderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The provider rejected the request because its credential is invalid, expired, or
/// revoked (research.md Decision 9). Mapped to a distinct Problem Details type so an
/// administrator, not the end user, is pointed at the fix.
/// </summary>
public sealed class AiProviderAuthenticationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The provider rate-limited this request (research.md Decision 9). <see cref="RetryAfter"/>
/// carries the vendor's own hint when one was supplied, mapped onto the HTTP `Retry-After`
/// header at the API boundary.
/// </summary>
public sealed class AiProviderRateLimitedException(string message, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The provider rejected this specific request as invalid or unusable (e.g. a 400 response
/// to a transcription upload) — distinct from <see cref="AiProviderUnavailableException"/>
/// (the provider itself is down) and <see cref="AiProviderAuthenticationException"/> (our
/// credential is bad). Mapped to a 400 Problem Details response so the end user, not an
/// administrator, is pointed at retrying with a different request (specs/032).
/// </summary>
public sealed class AiProviderRequestInvalidException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The AI-provider abstraction (docs/ARCHITECTURE.md &#167;9). One implementation per vendor
/// (OpenAI/Anthropic/GoogleGemini/OpenRouter — specs/005-multi-provider-ai-engine), selected
/// at runtime by <see cref="IAIProviderResolver"/> via a provider key, never resolved
/// directly by concrete type. This supersedes the single-provider constraint the legacy-
/// modernization spec's FR-022 originally placed here.
///
/// The single-arg <c>ChatModel</c>/<c>ImageModel</c>/<c>*Async(..., CancellationToken)</c>
/// members exist only for call sites that predate per-request model selection (Translate,
/// image generation, and <c>AppendMessageCommandHandler</c>'s attribution — none of which
/// are in specs/005-multi-provider-ai-engine's scope) and stay wired to the single, unkeyed
/// <c>IAIProvider</c> registration (OpenAI). New call sites use the model/parameter-aware
/// overloads via <see cref="IAIProviderResolver"/> instead.
/// </summary>
public interface IAIProvider
{
    string ProviderName { get; }

    /// <summary>The model used by legacy, pre-multi-provider call sites.</summary>
    string ChatModel { get; }

    /// <summary>Same reasoning as <see cref="ChatModel"/>. Providers with no image-generation support throw <see cref="NotSupportedException"/>.</summary>
    string ImageModel { get; }

    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Model/parameter-aware overload (FR-008/FR-014/FR-020) used by the multi-provider chat and comparison flows.</summary>
    Task<ChatCompletionResult> ChatAsync(IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Model/parameter-aware overload, same reasoning as the <see cref="ChatAsync(IReadOnlyList{ChatMessage},string,GenerationParametersDto?,CancellationToken)"/> overload.</summary>
    IAsyncEnumerable<StreamChunk> StreamChatAsync(IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, CancellationToken cancellationToken = default);

    Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);

    Task<Uri> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default);

    Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>A cheap operation (e.g. a models-list call), not a full chat completion (research.md Decision 7 — used by <c>ProviderHealthCheckHostedService</c>).</summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Model Discovery (research.md Decision 5) — surfaces a diff for an admin to review; never applied to the catalog automatically.</summary>
    Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default);
}
