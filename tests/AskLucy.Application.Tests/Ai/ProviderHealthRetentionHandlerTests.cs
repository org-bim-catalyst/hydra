using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.UpdateProviderHealthRetention;
using AskLucy.Application.Ai.Queries.GetProviderHealthRetention;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/082 — the stored retention window wins, configuration is the fallback, and a change is version-guarded.</summary>
public sealed class ProviderHealthRetentionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    private readonly IProviderHealthRetentionRepository _settings = Substitute.For<IProviderHealthRetentionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly IOptions<ProviderHealthCheckOptions> _options =
        OptionsFactory.Create(new ProviderHealthCheckOptions { Interval = TimeSpan.FromMinutes(2), RetentionDays = 30 });

    private GetProviderHealthRetentionQueryHandler GetHandler => new(_settings, _options);

    private UpdateProviderHealthRetentionCommandHandler UpdateHandler => new(
        _settings, _unitOfWork, _currentUser, _options, _time);

    public ProviderHealthRetentionHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
    }

    private static ProviderHealthRetentionSetting Stored(int days, byte[]? version = null)
    {
        var setting = ProviderHealthRetentionSetting.Create(days, "admin-1", Now.UtcDateTime);
        setting.RowVersion = version ?? Version;
        return setting;
    }

    [Fact]
    public async Task Get_ReturnsTheStoredWindowAndItsVersion()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored(14));

        var result = await GetHandler.Handle(new GetProviderHealthRetentionQuery(), TestContext.Current.CancellationToken);

        result.RetentionDays.Should().Be(14);
        result.IntervalMinutes.Should().Be(2);
        result.Source.Should().Be(ProviderHealthRetentionSource.Stored);
        result.RowVersion.Should().Be(Convert.ToBase64String(Version));
    }

    [Fact]
    public async Task Get_FallsBackToConfiguration_WhenTheRowIsMissing()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns((ProviderHealthRetentionSetting?)null);

        var result = await GetHandler.Handle(new GetProviderHealthRetentionQuery(), TestContext.Current.CancellationToken);

        result.RetentionDays.Should().Be(30);
        result.IntervalMinutes.Should().Be(2);
        result.Source.Should().Be(ProviderHealthRetentionSource.Configuration);
        result.RowVersion.Should().BeNull();
    }

    [Fact]
    public async Task Update_SavesTheNewWindow_AndAppendsOneAuditRow()
    {
        var setting = Stored(30);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(setting);

        var result = await UpdateHandler.Handle(
            new UpdateProviderHealthRetentionCommand(14, Version), TestContext.Current.CancellationToken);

        result.RetentionDays.Should().Be(14);
        result.Source.Should().Be(ProviderHealthRetentionSource.Stored);
        setting.RetentionDays.Should().Be(14);
        _settings.Received(1).ExpectRowVersion(setting, Version);
        _settings.Received(1).AddAudit(Arg.Is<ProviderHealthRetentionAuditLog>(entry =>
            entry != null
            && entry.ActorUserId == "admin-1"
            && entry.PreviousRetentionDays == 30
            && entry.NewRetentionDays == 14
            && entry.OccurredAtUtc == Now.UtcDateTime));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_RejectsAStaleVersion_WithoutChangingOrAuditing()
    {
        var setting = Stored(30);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(setting);

        var act = () => UpdateHandler.Handle(
            new UpdateProviderHealthRetentionCommand(14, [9, 9, 9, 9, 9, 9, 9, 9]), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        setting.RetentionDays.Should().Be(30);
        _settings.DidNotReceive().AddAudit(Arg.Any<ProviderHealthRetentionAuditLog>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_RejectsAMissingRow_WithoutAuditing()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns((ProviderHealthRetentionSetting?)null);

        var act = () => UpdateHandler.Handle(
            new UpdateProviderHealthRetentionCommand(14, Version), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _settings.DidNotReceive().AddAudit(Arg.Any<ProviderHealthRetentionAuditLog>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(0, false)]
    [InlineData(366, false)]
    public void Validator_EnforcesTheRange(int days, bool expected)
    {
        var result = new UpdateProviderHealthRetentionCommandValidator()
            .Validate(new UpdateProviderHealthRetentionCommand(days, Version));

        result.IsValid.Should().Be(expected);
    }

    [Fact]
    public void Validator_RequiresARowVersion()
    {
        var result = new UpdateProviderHealthRetentionCommandValidator()
            .Validate(new UpdateProviderHealthRetentionCommand(30, []));

        result.IsValid.Should().BeFalse();
    }
}
