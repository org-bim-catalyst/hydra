using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Application.Ai.Commands.UpdateAiCapabilitySettings;
using AskLucy.Application.Ai.Queries.GetAiCapabilitySettings;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/077 — the settings behind a capability's gear on the admin AI Capabilities page.</summary>
public sealed class CapabilitySettingsTests
{
    private const string Key = CapabilitySettingCatalog.IncludeConnectedBuildingsKey;

    private readonly IAiCapabilitySettingRepository _repository = Substitute.For<IAiCapabilitySettingRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly CapabilitySettingCatalog _catalog = new();

    public CapabilitySettingsTests() => _currentUser.UserId.Returns("admin-1");

    [Fact]
    public async Task Reader_UsesTheDefaultUntilAValueIsSaved()
    {
        var reader = new CapabilitySettingsReader(_repository, _catalog);

        (await reader.GetBooleanAsync(AiCapability.BoundaryVision, Key)).Should().BeTrue();

        _repository.GetAsync(AiCapability.BoundaryVision, Key, Arg.Any<CancellationToken>())
            .Returns(AiCapabilitySetting.Create(AiCapability.BoundaryVision, Key, "false", "admin-1"));
        (await reader.GetBooleanAsync(AiCapability.BoundaryVision, Key)).Should().BeFalse();
    }

    [Fact]
    public async Task Reader_RejectsASettingTheCapabilityNeverDeclared()
    {
        var reader = new CapabilitySettingsReader(_repository, _catalog);

        await reader.Invoking(r => r.GetBooleanAsync(AiCapability.Chat, Key)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Query_ListsEveryCapability_WithSettingsOnlyWhereDeclared()
    {
        _repository.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns([AiCapabilitySetting.Create(AiCapability.BoundaryVision, Key, "false", "admin-1")]);

        var result = await new GetAiCapabilitySettingsQueryHandler(_repository, _catalog).Handle(new GetAiCapabilitySettingsQuery(), default);

        result.Should().HaveCount(Enum.GetValues<AiCapability>().Length);
        result.Single(r => r.Capability == AiCapability.Chat).Settings.Should().BeEmpty();
        result.Single(r => r.Capability == AiCapability.BoundaryVision).Settings.Should().ContainSingle()
            .Which.Should().Match<AiCapabilitySettingDto>(s => s.Key == Key && s.Value == "false" && s.DefaultValue == "true");
    }

    [Theory]
    [InlineData(AiCapability.BoundaryVision, Key, "maybe")]
    [InlineData(AiCapability.Chat, Key, "true")]
    [InlineData(AiCapability.BoundaryVision, "unknownKey", "true")]
    public void Validator_RejectsUndeclaredKeysAndMistypedValues(AiCapability capability, string key, string value) =>
        new UpdateAiCapabilitySettingsCommandValidator(_catalog)
            .Validate(new UpdateAiCapabilitySettingsCommand(capability, new Dictionary<string, string> { [key] = value }))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validator_AcceptsADeclaredSwitch() =>
        new UpdateAiCapabilitySettingsCommandValidator(_catalog)
            .Validate(new UpdateAiCapabilitySettingsCommand(AiCapability.BoundaryVision, new Dictionary<string, string> { [Key] = "false" }))
            .IsValid.Should().BeTrue();

    [Fact]
    public async Task Handler_AddsARowTheFirstTimeAndChangesItAfter()
    {
        var handler = new UpdateAiCapabilitySettingsCommandHandler(
            _repository, _unitOfWork, _currentUser, Substitute.For<ILogger<UpdateAiCapabilitySettingsCommandHandler>>());
        var command = new UpdateAiCapabilitySettingsCommand(AiCapability.BoundaryVision, new Dictionary<string, string> { [Key] = "false" });

        await handler.Handle(command, default);
        _repository.Received(1).Add(Arg.Is<AiCapabilitySetting>(s => s.Key == Key && s.Value == "false"));

        var saved = AiCapabilitySetting.Create(AiCapability.BoundaryVision, Key, "false", "admin-1");
        _repository.ListByCapabilityAsync(AiCapability.BoundaryVision, Arg.Any<CancellationToken>()).Returns([saved]);
        await handler.Handle(command with { Values = new Dictionary<string, string> { [Key] = "true" } }, default);

        saved.Value.Should().Be("true");
        _repository.Received(1).Add(Arg.Any<AiCapabilitySetting>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
