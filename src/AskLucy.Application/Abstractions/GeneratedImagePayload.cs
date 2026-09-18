namespace AskLucy.Application.Abstractions;

/// <summary>
/// An image exactly as an AI provider returned it — vendors disagree on the form, and even one
/// vendor's models do (OpenAI's DALL·E models return a hosted URL; its GPT image models return
/// base64 only). <see cref="IAIProvider.GenerateImageAsync"/> reports whichever form it received
/// and never interprets it; <see cref="Ai.Images.GeneratedImageMaterializer"/> turns every form
/// into the same <see cref="GeneratedImage"/>, so a new vendor or response shape is one new
/// mapping in its provider, not a change to any caller.
/// </summary>
public abstract record GeneratedImagePayload
{
    private GeneratedImagePayload() { }

    /// <summary>A provider-hosted, usually short-lived address — downloaded before it expires, never shown to a user.</summary>
    public sealed record RemoteUrl(Uri Url) : GeneratedImagePayload;

    /// <summary>Raw base64 (e.g. OpenAI <c>b64_json</c>, Gemini <c>inlineData.data</c>), with the MIME type when the provider states one.</summary>
    public sealed record Base64(string Data, string? ContentType = null) : GeneratedImagePayload;

    /// <summary>An RFC 2397 <c>data:</c> URL (e.g. <c>data:image/png;base64,...</c>).</summary>
    public sealed record DataUrl(string Value) : GeneratedImagePayload;

    /// <summary>A binary response body.</summary>
    public sealed record Binary(byte[] Content, string? ContentType = null) : GeneratedImagePayload;
}

/// <summary>
/// A generated image normalised to bytes whose format was verified from the bytes themselves —
/// never trusted from a provider's declared MIME type or a URL's extension.
/// </summary>
public sealed record GeneratedImage(byte[] Content, string ContentType, string FileExtension);
