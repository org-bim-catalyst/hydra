using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Images;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Images;

/// <summary>The image model comes from the ImageGeneration capability assignment — never a hard-coded setting.</summary>
public sealed class ImageGenerationServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private readonly IAiCapabilityAssignmentRepository _assignments = Substitute.For<IAiCapabilityAssignmentRepository>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _gemini = Substitute.For<IAIProvider>();

    private ImageGenerationService CreateSut()
    {
        var resolver = new AiCapabilityProviderResolver(
            _assignments, _providers, _models, new DefaultProviderResolver(_providers, _models),
            NullLogger<AiCapabilityProviderResolver>.Instance);
        return new ImageGenerationService(resolver, _providers, _models, _providerResolver,
            new GeneratedImageMaterializer(Substitute.For<IRemoteFileDownloader>()));
    }

    [Fact]
    public async Task GenerateAsync_ShouldCallTheAssignedProvider_WithTheAssignedImageModel_AndReportAttribution()
    {
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = AIModel.Create(provider.Id, "gemini-3-pro-image-preview", "Gemini Image", null, null,
            new AIModelCapabilities(false, false, false, false, false, false, false, true, false), null, null, "test");

        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _assignments.GetByCapabilityAsync(AiCapability.ImageGeneration, Arg.Any<CancellationToken>())
            .Returns(AiCapabilityAssignment.Create(AiCapability.ImageGeneration, provider.Id, model.Id, "test"));
        _providerResolver.Resolve("google-gemini").Returns(_gemini);
        _gemini.ProviderName.Returns("Google Gemini");
        _gemini.GenerateImageAsync("a map", "gemini-3-pro-image-preview", Arg.Any<CancellationToken>())
            .Returns(new GeneratedImagePayload.Base64(Convert.ToBase64String(Png), "image/png"));

        var result = await CreateSut().GenerateAsync("a map", TestContext.Current.CancellationToken);

        result.Image.Content.Should().Equal(Png);
        result.ProviderName.Should().Be("Google Gemini");
        result.ModelKey.Should().Be("gemini-3-pro-image-preview");
    }

    [Fact]
    public async Task GenerateAsync_ShouldNotCallAnyProvider_WhenImageGenerationIsNotConfigured()
    {
        var act = () => CreateSut().GenerateAsync("a map", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AiCapabilityNotConfiguredException>();
        _providerResolver.DidNotReceiveWithAnyArgs().Resolve(default!);
    }
}
