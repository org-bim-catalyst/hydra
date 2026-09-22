using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// An administrator finds out at the moment they choose — not from a log days later — when an
/// assignment could never serve its capability. Image generation is the one capability that must
/// pin an image-capable model; every other capability keeps following the provider's default.
/// </summary>
public sealed class SetAiCapabilityAssignmentCommandValidatorTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly AIProvider _openai;
    private readonly AIModel _chatModel;
    private readonly AIModel _imageModel;

    public SetAiCapabilityAssignmentCommandValidatorTests()
    {
        _openai = AIProvider.Create("openai", "OpenAI", "test");
        _openai.SetCredential("ciphertext", null, "test");
        _openai.Enable("test");
        _chatModel = AIModel.Create(_openai.Id, "gpt-5", "GPT-5", null, null,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");
        _imageModel = AIModel.Create(_openai.Id, "gpt-image-2", "GPT Image 2", null, null,
            new AIModelCapabilities(false, false, false, false, false, false, false, true, false), null, null, "test");
        _openai.SetDefaultModel(_chatModel.Id, "test");

        _providers.GetByIdAsync(_openai.Id, Arg.Any<CancellationToken>()).Returns(_openai);
        _models.GetByIdAsync(_chatModel.Id, Arg.Any<CancellationToken>()).Returns(_chatModel);
        _models.GetByIdAsync(_imageModel.Id, Arg.Any<CancellationToken>()).Returns(_imageModel);
    }

    private Task<FluentValidation.Results.ValidationResult> ValidateAsync(SetAiCapabilityAssignmentCommand command) =>
        new SetAiCapabilityAssignmentCommandValidator(_providers, _models).ValidateAsync(command, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ShouldAccept_ImageGeneration_WithAnImageCapableModelOfThatProvider()
    {
        var result = await ValidateAsync(new(AiCapability.ImageGeneration, _openai.Id, _imageModel.Id));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReject_ImageGeneration_WithoutAPinnedModel()
    {
        var result = await ValidateAsync(new(AiCapability.ImageGeneration, _openai.Id));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "modelId");
    }

    [Fact]
    public async Task ShouldReject_ImageGeneration_WithAChatModel()
    {
        var result = await ValidateAsync(new(AiCapability.ImageGeneration, _openai.Id, _chatModel.Id));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "modelId");
    }

    [Fact]
    public async Task ShouldReject_AModelBelongingToADifferentProvider()
    {
        var gemini = AIProvider.Create("google-gemini", "Google Gemini", "test");
        var geminiModel = AIModel.Create(gemini.Id, "gemini-3-pro-image-preview", "Gemini Image", null, null,
            new AIModelCapabilities(false, false, false, false, false, false, false, true, false), null, null, "test");
        _models.GetByIdAsync(geminiModel.Id, Arg.Any<CancellationToken>()).Returns(geminiModel);

        var result = await ValidateAsync(new(AiCapability.ImageGeneration, _openai.Id, geminiModel.Id));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "modelId");
    }

    [Fact]
    public async Task ShouldStillAccept_OtherCapabilities_FollowingTheProvidersDefault()
    {
        var result = await ValidateAsync(new(AiCapability.TurnOrchestration, _openai.Id));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReject_AModelWithoutAProvider()
    {
        var result = await ValidateAsync(new(AiCapability.ImageGeneration, null, _imageModel.Id));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "modelId");
    }
}
