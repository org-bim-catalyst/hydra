using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Commands.UpdateNotificationPreferences;
using AskLucy.Application.Notifications.Queries.GetNotificationPreferences;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>specs/067 T135 — the effective merge, sparse storage, and the server-side lock on mandatory pairs.</summary>
public sealed class NotificationPreferencesHandlerTests
{
    private const string UserId = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPreferenceRepository _repository = new();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    public NotificationPreferencesHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private UpdateNotificationPreferencesCommandHandler UpdateHandler() =>
        new(_repository, _unitOfWork, _currentUser, _timeProvider);

    private static UpdateNotificationPreferencesCommand Update(params NotificationPreferenceChange[] changes) => new(changes);

    private static NotificationChannelPreferenceDto Channel(NotificationPreferencesDto dto, NotificationCategory category, NotificationChannel channel) =>
        dto.Categories.Single(c => c.Category == category).Channels.Single(c => c.Channel == channel);

    // --- GET: the effective merge ---

    [Fact]
    public async Task Get_WithNoOverrides_ReturnsTheCatalogueDefaults()
    {
        var handler = new GetNotificationPreferencesQueryHandler(_repository, _currentUser);

        var dto = await handler.Handle(new GetNotificationPreferencesQuery(), TestContext.Current.CancellationToken);

        Channel(dto, NotificationCategory.Workflow, NotificationChannel.InApp).Should().Be(new NotificationChannelPreferenceDto(NotificationChannel.InApp, true, false));
        Channel(dto, NotificationCategory.Workflow, NotificationChannel.Email).Enabled.Should().Be(
            NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email));
        dto.Categories.Should().OnlyContain(c => c.Frequency == DeliveryFrequency.Immediate
            && c.AvailableFrequencies.Count == 1 && c.AvailableFrequencies[0] == DeliveryFrequency.Immediate);
    }

    [Fact]
    public async Task Get_OmitsCategoriesNothingEmitsYet()
    {
        var handler = new GetNotificationPreferencesQueryHandler(_repository, _currentUser);

        var dto = await handler.Handle(new GetNotificationPreferencesQuery(), TestContext.Current.CancellationToken);

        dto.Categories.Select(c => c.Category).Should().NotContain([NotificationCategory.Billing, NotificationCategory.Conversation]);
        dto.Categories.Select(c => c.Category).Should().Contain(
            [NotificationCategory.Security, NotificationCategory.Account, NotificationCategory.Agent, NotificationCategory.Workflow,
             NotificationCategory.Document, NotificationCategory.KnowledgeBase, NotificationCategory.Memory, NotificationCategory.System]);
    }

    [Fact]
    public async Task Get_ShowsMandatoryPairsLockedAndOn()
    {
        var handler = new GetNotificationPreferencesQueryHandler(_repository, _currentUser);

        var dto = await handler.Handle(new GetNotificationPreferencesQuery(), TestContext.Current.CancellationToken);

        Channel(dto, NotificationCategory.Security, NotificationChannel.Email).Should().Be(
            new NotificationChannelPreferenceDto(NotificationChannel.Email, true, true));
    }

    [Fact]
    public async Task Get_AppliesTheCallersOverride()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);
        _repository.Seed(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail);
        var handler = new GetNotificationPreferencesQueryHandler(_repository, _currentUser);

        var dto = await handler.Handle(new GetNotificationPreferencesQuery(), TestContext.Current.CancellationToken);

        Channel(dto, NotificationCategory.Workflow, NotificationChannel.Email).Enabled.Should().Be(!defaultEmail);
    }

    // --- PUT: sparse storage ---

    [Fact]
    public async Task Update_AwayFromTheDefault_SavesAnOverride_AndReturnsTheNewEffectiveState()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);

        var dto = await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail)),
            TestContext.Current.CancellationToken);

        _repository.Rows.Should().ContainSingle(r => r.Category == NotificationCategory.Workflow && r.Channel == NotificationChannel.Email && r.IsEnabled == !defaultEmail);
        Channel(dto, NotificationCategory.Workflow, NotificationChannel.Email).Enabled.Should().Be(!defaultEmail);
        await _unitOfWork.Received(1).TrySaveChangesAsync(INotificationPreferenceRepository.UserCategoryChannelIndexName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_BackToTheDefault_DeletesTheOverride()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);
        _repository.Seed(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail);

        await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, defaultEmail)),
            TestContext.Current.CancellationToken);

        _repository.Rows.Should().BeEmpty("a value equal to the default is stored as no row at all");
    }

    [Fact]
    public async Task Update_TheDefaultValueWithNoOverride_StoresNothing()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);

        await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, defaultEmail)),
            TestContext.Current.CancellationToken);

        _repository.Rows.Should().BeEmpty();
        _repository.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_AnExistingOverride_ChangesItInPlace()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Memory, NotificationChannel.Email);
        _repository.Seed(NotificationCategory.Memory, NotificationChannel.Email, !defaultEmail);
        var inApp = !NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Memory, NotificationChannel.InApp);

        await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Memory, NotificationChannel.InApp, inApp)),
            TestContext.Current.CancellationToken);

        _repository.Rows.Should().HaveCount(2);
    }

    // --- PUT: mandatory pairs are enforced on the server ---

    [Fact]
    public async Task Update_DisablingAMandatoryPair_Throws422Style_WithTheChangeIndex_AndAppliesNothing()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);

        var act = () => UpdateHandler().Handle(
            Update(
                new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail),
                new NotificationPreferenceChange(NotificationCategory.Security, NotificationChannel.Email, Enabled: false)),
            TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<NotificationPreferenceRejectedException>()).Which;
        thrown.Errors.Should().ContainKey("changes[1]").And.NotContainKey("changes[0]");
        _repository.Added.Should().BeEmpty("the request is atomic: the valid change must not be applied");
        await _unitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_EnablingAMandatoryPair_IsAcceptedAndStoresNothing()
    {
        await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Security, NotificationChannel.Email, Enabled: true)),
            TestContext.Current.CancellationToken);

        _repository.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_DisablingAPairNothingEmits_IsRefused()
    {
        var act = () => UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Billing, NotificationChannel.Email, Enabled: false)),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotificationPreferenceRejectedException>();
    }

    [Fact]
    public async Task Update_WhenTheFirstSaveLosesAUniquenessRace_ReadsAgainAndAppliesOnce()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false, true);

        await UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail)),
            TestContext.Current.CancellationToken);

        await _unitOfWork.Received(2).TrySaveChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenTheRaceIsLostTwice_Fails_InsteadOfSilentlyDroppingTheChange()
    {
        var defaultEmail = NotificationTypeCatalog.DefaultEnabled(NotificationCategory.Workflow, NotificationChannel.Email);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var act = () => UpdateHandler().Handle(
            Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, !defaultEmail)),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- validator ---

    [Fact]
    public void Validator_AcceptsOneChange_AndAllTwentyDistinctPairs()
    {
        var all = Enum.GetValues<NotificationCategory>()
            .SelectMany(c => Enum.GetValues<NotificationChannel>().Select(ch => new NotificationPreferenceChange(c, ch, true)))
            .ToList();
        var validator = new UpdateNotificationPreferencesCommandValidator();

        validator.Validate(new UpdateNotificationPreferencesCommand([all[0]])).IsValid.Should().BeTrue();
        validator.Validate(new UpdateNotificationPreferencesCommand(all)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsNoChanges_AndMoreThanFortyChanges()
    {
        var change = new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, true);
        var validator = new UpdateNotificationPreferencesCommandValidator();

        validator.Validate(new UpdateNotificationPreferencesCommand([])).IsValid.Should().BeFalse();
        validator.Validate(new UpdateNotificationPreferencesCommand(Enumerable.Repeat(change, UpdateNotificationPreferencesCommandValidator.MaxChanges + 1).ToList()))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsAnythingButImmediateFrequency()
    {
        var command = Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, true, DeliveryFrequency.DailyDigest));

        new UpdateNotificationPreferencesCommandValidator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_AcceptsImmediateFrequency()
    {
        var command = Update(new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, true, DeliveryFrequency.Immediate));

        new UpdateNotificationPreferencesCommandValidator().Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsTheSamePairTwice()
    {
        var change = new NotificationPreferenceChange(NotificationCategory.Workflow, NotificationChannel.Email, true);

        new UpdateNotificationPreferencesCommandValidator().Validate(Update(change, change)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsAnUnknownCategory()
    {
        var command = Update(new NotificationPreferenceChange((NotificationCategory)999, NotificationChannel.Email, true));

        new UpdateNotificationPreferencesCommandValidator().Validate(command).IsValid.Should().BeFalse();
    }

    /// <summary>A repository over a list: no EF Core in Application tests.</summary>
    private sealed class InMemoryPreferenceRepository : INotificationPreferenceRepository
    {
        public List<NotificationPreference> Rows { get; } = [];

        public List<NotificationPreference> Added { get; } = [];

        public void Seed(NotificationCategory category, NotificationChannel channel, bool enabled) =>
            Rows.Add(NotificationPreference.Create(UserId, category, channel, enabled, DeliveryFrequency.Immediate, Now.UtcDateTime));

        public Task<IReadOnlyList<PreferenceOverride>> GetOverridesAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PreferenceOverride>>([.. Rows.Select(r => new PreferenceOverride(r.Category, r.Channel, r.IsEnabled))]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<PreferenceOverride>>> GetOverridesForUsersAsync(
            IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NotificationPreference>> GetTrackedAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationPreference>>([.. Rows]);

        public void Add(NotificationPreference preference)
        {
            Added.Add(preference);
            Rows.Add(preference);
        }

        public Task DeleteAsync(
            string userId, IReadOnlyCollection<(NotificationCategory Category, NotificationChannel Channel)> pairs, CancellationToken cancellationToken)
        {
            Rows.RemoveAll(r => pairs.Contains((r.Category, r.Channel)));
            return Task.CompletedTask;
        }
    }
}
