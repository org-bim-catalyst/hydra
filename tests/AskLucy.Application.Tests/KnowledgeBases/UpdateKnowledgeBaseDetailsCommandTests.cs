using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;
using AskLucy.Domain.KnowledgeBases;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

public sealed class UpdateKnowledgeBaseDetailsCommandHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private UpdateKnowledgeBaseDetailsCommandHandler CreateHandler() => new(_repository, _auditLogRepository, _unitOfWork, _currentUser);

    [Fact]
    public async Task Handle_ShouldReplaceEveryField()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("Old", "user-1", "user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var result = await CreateHandler().Handle(
            new UpdateKnowledgeBaseDetailsCommand(knowledgeBase.Id, "New", "desc", "#000", "icon", null, ["a", "b"], "notes"),
            CancellationToken.None);

        result.Name.Should().Be("New");
        result.Tags.Should().BeEquivalentTo(["a", "b"]);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRemoveTagsNoLongerInTheDesiredSet()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "user-1", "user-1");
        knowledgeBase.AddTag("old-tag", "user-1", "user-1");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        await CreateHandler().Handle(
            new UpdateKnowledgeBaseDetailsCommand(knowledgeBase.Id, "KB", null, null, null, null, ["new-tag"], null),
            CancellationToken.None);

        knowledgeBase.Tags.Select(t => t.Value).Should().BeEquivalentTo(["new-tag"]);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenKnowledgeBaseIsOwnedByAnotherUser()
    {
        _currentUser.UserId.Returns("user-1");
        var knowledgeBase = KnowledgeBase.Create("KB", "someone-else", "someone-else");
        _repository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);

        var act = () => CreateHandler().Handle(
            new UpdateKnowledgeBaseDetailsCommand(knowledgeBase.Id, "New", null, null, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
