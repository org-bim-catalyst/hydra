using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.OperationalFailures.Investigations.GetChatInvestigation;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static AskLucy.Application.Tests.OperationalFailures.OperationalFailureTestData;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 research D15 — the reference gate, the metadata/content split, and the access event written before any content is read.</summary>
public sealed class ChatInvestigationTests
{
    private const string Owner = "owner-1";
    private const string Viewer = "admin-1";

    private readonly IOperationalFailureStore _store = Substitute.For<IOperationalFailureStore>();
    private readonly IOperationalFailureReferenceLookup _references = Substitute.For<IOperationalFailureReferenceLookup>();
    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUserContentAccessEventRepository _accessEvents = Substitute.For<IUserContentAccessEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEffectivePermissionResolver _permissions = Substitute.For<IEffectivePermissionResolver>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ICorrelationIdAccessor _correlation = Substitute.For<ICorrelationIdAccessor>();

    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly UserChat _chat = UserChat.Create("Budget review", Owner, null, Owner);
    private readonly Message _question;
    private readonly Message _failedReply;

    public ChatInvestigationTests()
    {
        _chat.Id = Guid.NewGuid();
        _question = ChatMessage(MessageRole.User, "What is the budget?", Now.AddMinutes(-2));
        _failedReply = ChatMessage(MessageRole.Assistant, "The budget is", Now.AddMinutes(-1));

        _store.FindItemReferencesAsync(_incidentId, InvestigatedItemType.Chat, _chat.Id, Arg.Any<CancellationToken>())
            .Returns([Occurrence(_incidentId, new OperationalFailureReferences { ChatId = _chat.Id, MessageId = _failedReply.Id })]);
        _chats.GetByIdIncludingDeletedAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns(_chat);
        _messages.ListOutlineByChatIdAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns([
            new MessageOutline(_question.Id, MessageRole.User, _question.CreatedAtUtc),
            new MessageOutline(_failedReply.Id, MessageRole.Assistant, _failedReply.CreatedAtUtc),
        ]);
        _messages.ListByChatIdAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns([_question, _failedReply]);
        _references.FindUsersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, UserReference> { [Owner] = new(Owner, "Ada Lovelace", "ada@example.com", IsDeleted: false) });
        _currentUser.UserId.Returns(Viewer);
        _correlation.Current.Returns("corr-request");
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView);
    }

    private Message ChatMessage(MessageRole role, string content, DateTime createdAtUtc)
    {
        var message = Message.Create(_chat.Id, role, MessageKind.Text, content, null, Owner);
        message.Id = Guid.NewGuid();
        message.CreatedAtUtc = createdAtUtc;
        return message;
    }

    private void GivenPermissions(params string[] keys) =>
        _permissions.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(PermissionSet.Create(keys));

    private GetChatInvestigationQueryHandler Handler() => new(
        _store, new OperationalFailureReadModelBuilder(_store, _references), _chats, _messages, _accessEvents, _unitOfWork,
        _permissions, _currentUser, _correlation, new FakeTimeProvider(Now));

    private Task<ChatInvestigationDto> Investigate(Guid? chatId = null) =>
        Handler().Handle(new GetChatInvestigationQuery(_incidentId, chatId ?? _chat.Id), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AChatTheIncidentDoesNotReferenceIsNotFound_EvenForAContentHolder()
    {
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView);
        var otherChatId = Guid.NewGuid();
        _store.FindItemReferencesAsync(_incidentId, InvestigatedItemType.Chat, otherChatId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<OperationalFailureOccurrence>?)null);

        var act = () => Investigate(otherChatId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _messages.DidNotReceiveWithAnyArgs().ListByChatIdAsync(default, default);
        await _accessEvents.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task WithoutTheContentPermission_ReturnsMetadataOnlyAndRecordsNothing()
    {
        var result = await Investigate();

        result.Transcript.Should().BeNull();
        result.Chat.Should().Be(new ChatInvestigationChatDto(
            _chat.Id,
            "Budget review",
            new UserRefDto(Owner, "Ada Lovelace", "ada@example.com", UserRefStatus.Active),
            _chat.CreatedAtUtc,
            _chat.ModifiedAtUtc ?? _chat.CreatedAtUtc,
            MessageCount: 2,
            Deleted: false));
        result.FailurePoints.Should().ContainSingle().Which.Should().Match<ChatFailurePointDto>(p => p.TurnNumber == 1 && p.MessageId == _failedReply.Id);
        await _messages.DidNotReceiveWithAnyArgs().ListByChatIdAsync(default, default);
        await _accessEvents.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task WithTheContentPermission_CommitsTheAccessEventBeforeReadingAnyContent()
    {
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView);
        UserContentAccessEvent? recorded = null;
        await _accessEvents.AddAsync(Arg.Do<UserContentAccessEvent>(e => recorded = e), Arg.Any<CancellationToken>());

        var result = await Investigate();

        Received.InOrder(() =>
        {
            _accessEvents.AddAsync(Arg.Any<UserContentAccessEvent>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _messages.ListByChatIdAsync(_chat.Id, Arg.Any<CancellationToken>());
        });
        await _accessEvents.ReceivedWithAnyArgs(1).AddAsync(default!, default);
        recorded.Should().NotBeNull();
        recorded!.ViewerUserId.Should().Be(Viewer);
        recorded.OwnerUserId.Should().Be(Owner);
        recorded.ItemType.Should().Be(InvestigatedItemType.Chat);
        recorded.ItemId.Should().Be(_chat.Id);
        recorded.IncidentId.Should().Be(_incidentId);
        recorded.CorrelationId.Should().Be("corr-request");
        result.Transcript.Should().Equal(
            new ChatTranscriptMessageDto(_question.Id, "user", _question.CreatedAtUtc, "What is the budget?", IsFailedTurn: false),
            new ChatTranscriptMessageDto(_failedReply.Id, "assistant", _failedReply.CreatedAtUtc, "The budget is", IsFailedTurn: true));
    }

    [Fact]
    public async Task AnOwnerReadingTheirOwnChat_IsNotRecordedAsAnAccess()
    {
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView);
        _currentUser.UserId.Returns(Owner);

        var result = await Investigate();

        result.Transcript.Should().HaveCount(2);
        await _accessEvents.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task WhenTheAccessEventCannotBeSaved_TheRequestFailsAndNoContentIsRead()
    {
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database unavailable"));

        var act = () => Investigate();

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _messages.DidNotReceiveWithAnyArgs().ListByChatIdAsync(default, default);
    }

    [Fact]
    public async Task ASoftDeletedChat_IsShownAsDeletedWithNoTranscript()
    {
        GivenPermissions(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView);
        _chat.SoftDelete(Owner);

        var result = await Investigate();

        result.Chat.Deleted.Should().BeTrue();
        result.Transcript.Should().BeNull();
        await _accessEvents.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _messages.DidNotReceiveWithAnyArgs().ListByChatIdAsync(default, default);
    }

    [Fact]
    public async Task APurgedChat_KeepsItsFailurePointsWithAnErasedOwner()
    {
        _chats.GetByIdIncludingDeletedAsync(_chat.Id, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var result = await Investigate();

        result.Chat.Deleted.Should().BeTrue();
        result.Chat.Owner.Should().Be(UserRefDto.Erased);
        result.Chat.MessageCount.Should().Be(0);
        result.FailurePoints.Should().ContainSingle().Which.TurnNumber.Should().BeNull();
    }
}
