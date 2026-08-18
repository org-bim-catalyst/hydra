using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Queries.CompareVersions;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

public sealed class CompareVersionsQueryHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private CompareVersionsQueryHandler CreateHandler() => new(_promptRepository, _currentUser);

    private static PromptContentSnapshot Content(string userInstructions) => new(
        "System v1", null, userInstructions, null, null, null, null, null, null, null, null, false);

    [Fact]
    public async Task Handle_ShouldReportOnlyChangedFields()
    {
        _currentUser.UserId.Returns(OwnerId);

        var (prompt, version1) = Prompt.Create(
            OwnerId, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content("Summarize {{document}}."),
            [new PromptVariableDefinition("document", null, PromptVariableType.String, true, null, null, null, 0)], OwnerId);
        var version2 = prompt.ApplyEdit(
            Content("Summarize {{document}} briefly."),
            [new PromptVariableDefinition("document", null, PromptVariableType.String, true, null, null, null, 0)], null, OwnerId);

        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, 1, Arg.Any<CancellationToken>()).Returns(version1);
        _promptRepository.GetVersionAsync(prompt.Id, 2, Arg.Any<CancellationToken>()).Returns(version2);

        var result = await CreateHandler().Handle(new CompareVersionsQuery(prompt.Id, 1, 2), CancellationToken.None);

        result.From.VersionNumber.Should().Be(1);
        result.To.VersionNumber.Should().Be(2);
        result.Differences.Should().ContainSingle(d => d.FieldName == nameof(PromptVersion.UserInstructions));
        result.Differences.Single().FromValue.Should().Be("Summarize {{document}}.");
        result.Differences.Single().ToValue.Should().Be("Summarize {{document}} briefly.");
    }

    [Fact]
    public async Task Handle_ShouldReportNoDifferences_WhenBothVersionsAreIdentical()
    {
        _currentUser.UserId.Returns(OwnerId);

        var (prompt, version1) = Prompt.Create(
            OwnerId, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content("Summarize {{document}}."),
            [new PromptVariableDefinition("document", null, PromptVariableType.String, true, null, null, null, 0)], OwnerId);

        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, 1, Arg.Any<CancellationToken>()).Returns(version1);

        var result = await CreateHandler().Handle(new CompareVersionsQuery(prompt.Id, 1, 1), CancellationToken.None);

        result.Differences.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenAVersionDoesNotExist()
    {
        _currentUser.UserId.Returns(OwnerId);

        var (prompt, version1) = Prompt.Create(
            OwnerId, "Prompt", null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            Content("Hello."), [], OwnerId);

        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, 1, Arg.Any<CancellationToken>()).Returns(version1);
        _promptRepository.GetVersionAsync(prompt.Id, 99, Arg.Any<CancellationToken>()).Returns((PromptVersion?)null);

        var act = () => CreateHandler().Handle(new CompareVersionsQuery(prompt.Id, 1, 99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
