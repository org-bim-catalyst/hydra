using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Images;

/// <summary>A generated image plus which provider and model produced it (message attribution, FR-016).</summary>
public sealed record ImageGenerationResult(GeneratedImage Image, string ProviderName, string ModelKey);

/// <summary>
/// The one way the platform generates an image — the chat's image command and the Site Analysis
/// Agent's schematic map both come through here, so neither chooses a provider or model itself.
/// </summary>
public interface IImageGenerationService
{
    /// <exception cref="AiCapabilityNotConfiguredException">No image-capable model is assigned to <see cref="AiCapability.ImageGeneration"/>.</exception>
    Task<ImageGenerationResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves <see cref="AiCapability.ImageGeneration"/> (the administrator's provider + pinned
/// image model), calls that provider, and normalises whatever form it returned
/// (<see cref="GeneratedImageMaterializer"/>). Replaces two hard-coded model settings
/// (<c>Ai:OpenAI:ImageModel</c> and the Site Analysis Agent's own <c>SiteAnalysis</c> section)
/// that bypassed the capability assignments every other AI task already uses.
/// </summary>
public sealed class ImageGenerationService(
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    GeneratedImageMaterializer materializer) : IImageGenerationService
{
    public async Task<ImageGenerationResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.ImageGeneration, cancellationToken);

        var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
            ?? throw new AiCapabilityNotConfiguredException(AiCapability.ImageGeneration, "the assigned provider no longer exists");
        var model = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
            ?? throw new AiCapabilityNotConfiguredException(AiCapability.ImageGeneration, "the assigned model no longer exists");

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);
        var payload = await aiProvider.GenerateImageAsync(prompt, model.ModelKey, cancellationToken);
        var image = await materializer.MaterializeAsync(payload, cancellationToken);

        return new ImageGenerationResult(image, aiProvider.ProviderName, model.ModelKey);
    }
}
