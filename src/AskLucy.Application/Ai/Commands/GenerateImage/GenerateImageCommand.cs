using MediatR;

namespace AskLucy.Application.Ai.Commands.GenerateImage;

/// <summary>
/// Generates an image for the current user and stores it as their own <c>Document</c>. The
/// provider's response (often a short-lived hosted URL, or raw base64) is never handed to the
/// client — files are only ever served through the platform's signed download URLs.
/// </summary>
public sealed record GenerateImageCommand(string Prompt) : IRequest<GeneratedImageDto>;

/// <summary>The stored image, plus the provider/model that produced it (message attribution, FR-016).</summary>
public sealed record GeneratedImageDto(Guid DocumentId, string ProviderName, string ModelKey);
