namespace AskLucy.Application.Abstractions;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>
/// Thrown after the single automatic retry (research.md Topic 4 / FR-032) still fails.
/// The WebAPI layer maps this to an <c>ai-provider-unavailable</c> Problem Details
/// response — callers must never see the underlying provider exception.
/// </summary>
public sealed class AiProviderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The single AI-provider abstraction (docs/ARCHITECTURE.md &#167;9). Exactly one
/// implementation (<c>OpenAIProvider</c>) exists in this migration — FR-022 explicitly
/// forbids introducing additional providers or model switching here.
/// </summary>
public interface IAIProvider
{
    /// <summary>The provider's display name (e.g., "OpenAI") — recorded against assistant messages (specs/002-chat-history-management FR-016), never a vendor SDK type leaking past this interface.</summary>
    string ProviderName { get; }

    /// <summary>The model identifier used for chat/translation completions — recorded against assistant messages (FR-016).</summary>
    string ChatModel { get; }

    /// <summary>The model identifier used for image generation — recorded against assistant messages (FR-016).</summary>
    string ImageModel { get; }

    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    Task<Uri> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);

    Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default);
}
