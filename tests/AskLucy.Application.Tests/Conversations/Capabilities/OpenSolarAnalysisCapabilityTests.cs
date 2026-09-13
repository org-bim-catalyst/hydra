using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/052-solar-analysis T059 — mirrors <see cref="LoadViewerContentCapabilityTests"/>'
/// structure: schema validation, success echo, `no-active-site` refusal (FR-035),
/// `viewer-unavailable` refusal (FR-036), and — the mechanism FR-034 actually depends on — that
/// the capability's own instruction wording tells Lucy to describe the display rather than
/// reciting figures (T062).
/// </summary>
public sealed class OpenSolarAnalysisCapabilityTests
{
    private static AgentToolExecutionContext ContextFor(Guid? userChatId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), userChatId);

    private static UserChat BuildChatWithActiveLocation()
    {
        var chat = UserChat.Create("Test chat", "user-1", null, "user-1");
        chat.SetActiveLocation(25.2048, 55.2708, "Dubai", 0.95, "agent");
        return chat;
    }

    private static (OpenSolarAnalysisCapability Capability, IUserChatRepository Repository) BuildCapability()
    {
        var repository = Substitute.For<IUserChatRepository>();
        return (new OpenSolarAnalysisCapability(repository), repository);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceedWithOpenedTrue_WhenAnActiveSiteExists()
    {
        var (capability, repository) = BuildCapability();
        var chatId = Guid.NewGuid();
        repository.GetByIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(BuildChatWithActiveLocation());

        var input = JsonDocument.Parse("""{"date":"2026-09-13","timeOfDay":"14:00"}""");
        var result = await capability.ExecuteAsync(ContextFor(chatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var output = result.Output!.RootElement;
        output.GetProperty("opened").GetBoolean().Should().BeTrue();
        output.GetProperty("date").GetString().Should().Be("2026-09-13");
        output.GetProperty("timeOfDay").GetString().Should().Be("14:00");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDefaultDateAndTimeOfDay_WhenOmitted()
    {
        var (capability, repository) = BuildCapability();
        var chatId = Guid.NewGuid();
        repository.GetByIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(BuildChatWithActiveLocation());

        var input = JsonDocument.Parse("{}");
        var result = await capability.ExecuteAsync(ContextFor(chatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("opened").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuseWithNoActiveSite_WhenTheChatHasNoActiveLocation_FR035()
    {
        var (capability, repository) = BuildCapability();
        var chatId = Guid.NewGuid();
        repository.GetByIdAsync(chatId, Arg.Any<CancellationToken>()).Returns(UserChat.Create("Test chat", "user-1", null, "user-1"));

        var input = JsonDocument.Parse("{}");
        var result = await capability.ExecuteAsync(ContextFor(chatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue("this is a legitimate refusal outcome, not an execution error");
        var output = result.Output!.RootElement;
        output.GetProperty("opened").GetBoolean().Should().BeFalse();
        output.GetProperty("reason").GetString().Should().Be("no-active-site");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuseWithNoActiveSite_WhenTheChatCannotBeFound()
    {
        var (capability, repository) = BuildCapability();
        var chatId = Guid.NewGuid();
        repository.GetByIdAsync(chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var input = JsonDocument.Parse("{}");
        var result = await capability.ExecuteAsync(ContextFor(chatId), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reason").GetString().Should().Be("no-active-site");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRefuseWithViewerUnavailable_WhenTheExecutionHasNoLinkedChat_FR036()
    {
        var (capability, _) = BuildCapability();

        var input = JsonDocument.Parse("{}");
        var result = await capability.ExecuteAsync(ContextFor(null), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var output = result.Output!.RootElement;
        output.GetProperty("opened").GetBoolean().Should().BeFalse();
        output.GetProperty("reason").GetString().Should().Be("viewer-unavailable");
    }

    [Fact]
    public void IsAvailable_ShouldAlwaysBeTrue_SoTheNoActiveSiteOutcomeIsNarratedRatherThanHidden()
    {
        var (capability, _) = BuildCapability();
        capability.IsAvailable(TurnContext.Empty("user-1", Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public void IsOfferable_ShouldAlwaysBeFalse()
    {
        var (capability, _) = BuildCapability();
        capability.IsOfferable(TurnContext.Empty("user-1", Guid.NewGuid()), null!).Should().BeFalse();
    }

    /// <summary>
    /// T062, FR-033, FR-034 — the ONLY mechanism that makes Lucy describe the display rather than
    /// reciting the figures the panel already shows: the capability's own instruction text. This
    /// assertion is what prevents a later edit from silently turning it into a figure-reciter.
    /// </summary>
    [Fact]
    public void UsageGuidance_ShouldInstructDescribingTheDisplay_NotRecitingFigures()
    {
        var (capability, _) = BuildCapability();

        capability.UsageGuidance.Should().Contain("describe", "Lucy must describe what is on screen");
        capability.UsageGuidance.Should().ContainAny("do NOT recite", "do not recite");
        capability.UsageGuidance.Should().Contain("azimuth");
        capability.UsageGuidance.Should().Contain("altitude");
        capability.UsageGuidance.Should().Contain("sunrise");
        capability.UsageGuidance.Should().Contain("sunset");
    }

    [Fact]
    public void CapabilityKey_ShouldBeOpenSolarAnalysis()
    {
        OpenSolarAnalysisCapability.CapabilityKey.Should().Be("open_solar_analysis");
    }

    [Fact]
    public void InputSchemaJson_ShouldRequireNoCoordinates()
    {
        var (capability, _) = BuildCapability();
        capability.InputSchemaJson.Should().NotContain("latitude");
        capability.InputSchemaJson.Should().NotContain("longitude");
    }
}
