using AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;
using AskLucy.Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Application.Tests.CustomModels;

public sealed class SubmitCustomModelDeploymentCommandValidatorTests
{
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";
    private const string Destination = "Models/supertonic-3";

    private readonly SubmitCustomModelDeploymentCommandValidator _validator;

    public SubmitCustomModelDeploymentCommandValidatorTests()
    {
        var options = Substitute.For<IOptionsMonitor<CustomModelsOptions>>();
        options.CurrentValue.Returns(new CustomModelsOptions());
        _validator = new SubmitCustomModelDeploymentCommandValidator(options);
    }

    [Fact]
    public async Task Valid_WithoutName_Passes()
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(Source, Destination, null), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Source_Required_KeyedBySource(string? source)
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(source!, Destination, null), TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "source").Which.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public async Task Source_TooLong_KeyedBySource()
    {
        var source = Source + "/tree/" + new string('a', SubmitCustomModelDeploymentCommandValidator.MaxSourceLength);

        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(source, Destination, null), TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "source").Which.ErrorMessage.Should().Contain("at most");
    }

    [Theory]
    [InlineData("https://example.com/Supertone/supertonic-3")]
    [InlineData("ftp://huggingface.co/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co.evil.test/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co/datasets/owner/repo")]
    public async Task Source_NotAHuggingFaceModel_KeyedBySource(string source)
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(source, Destination, null), TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "source");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Models/../web.config")]
    [InlineData("/Models/x")]
    [InlineData("wwwroot/x")]
    [InlineData("Models")]
    public async Task Destination_Invalid_KeyedByDestination(string destination)
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(Source, destination, null), TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "destination");
    }

    [Theory]
    [InlineData("Supertonic 3")]
    [InlineData("kokoro_v1.0-onnx")]
    [InlineData("مودل")]
    public async Task Name_Allowed(string name)
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(Source, Destination, name), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("-leading-dash")]
    [InlineData("has/slash")]
    [InlineData("tab\tinside")]
    public async Task Name_Rejected_KeyedByName(string name)
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(Source, Destination, name), TestContext.Current.CancellationToken);

        result.Errors.Should().Contain(e => e.PropertyName == "name");
    }

    [Fact]
    public async Task Name_TooLong_KeyedByName()
    {
        var result = await _validator.ValidateAsync(new SubmitCustomModelDeploymentCommand(Source, Destination, new string('a', 101)), TestContext.Current.CancellationToken);

        result.Errors.Should().Contain(e => e.PropertyName == "name");
    }
}
