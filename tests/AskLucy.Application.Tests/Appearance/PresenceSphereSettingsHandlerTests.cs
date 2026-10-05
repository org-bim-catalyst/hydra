using AskLucy.Application.Abstractions;
using AskLucy.Application.Appearance;
using AskLucy.Application.Appearance.Commands.UpdatePresenceSphereSettings;
using AskLucy.Application.Appearance.Queries.GetPresenceSphereSettings;
using AskLucy.Application.Users;
using AskLucy.Domain.Appearance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Appearance;

public sealed class PresenceSphereSettingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IPresenceSphereSettingsRepository _settings = Substitute.For<IPresenceSphereSettingsRepository>();
    private readonly IUserProfileRepository _profiles = Substitute.For<IUserProfileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _time = new(Now);

    public PresenceSphereSettingsHandlerTests() => _currentUser.UserId.Returns("admin-1");

    private UpdatePresenceSphereSettingsCommandHandler UpdateHandler(ILogger<UpdatePresenceSphereSettingsCommandHandler>? logger = null) =>
        new(_settings, _profiles, _unitOfWork, _currentUser, _time, logger ?? NullLogger<UpdatePresenceSphereSettingsCommandHandler>.Instance);

    [Fact]
    public async Task Query_ShouldReturnTheDefaults_WhenNothingIsSaved()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns((PresenceSphereSettings?)null);

        var dto = await new GetPresenceSphereSettingsQueryHandler(_settings, _profiles)
            .Handle(new GetPresenceSphereSettingsQuery(), CancellationToken.None);

        dto.IsDefault.Should().BeTrue();
        (dto.DotSizeMultiplier, dto.CardFillPercent, dto.ZoomEnabled).Should().Be((1.00m, 75, false));
        dto.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task Query_ShouldReturnTheSavedValuesAndTheAdministratorsName_NeverTheirEmail()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(PresenceSphereSettings.Create(0.5m, 60, true, "admin-1", Now.UtcDateTime));
        _profiles.GetByIdAsync("admin-1", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("admin-1", "ada@example.com", "Ada", "Lovelace", new DateOnly(1990, 1, 1), false, null));

        var dto = await new GetPresenceSphereSettingsQueryHandler(_settings, _profiles)
            .Handle(new GetPresenceSphereSettingsQuery(), CancellationToken.None);

        dto.IsDefault.Should().BeFalse();
        dto.ModifiedBy.Should().Be("Ada Lovelace");
        dto.ModifiedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Query_ShouldLeaveTheNameOut_WhenTheAdministratorCanNoLongerBeFound()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(PresenceSphereSettings.Create(0.5m, 60, true, "gone", Now.UtcDateTime));
        _profiles.GetByIdAsync("gone", Arg.Any<CancellationToken>()).Returns((UserProfileDto?)null);

        var dto = await new GetPresenceSphereSettingsQueryHandler(_settings, _profiles)
            .Handle(new GetPresenceSphereSettingsQuery(), CancellationToken.None);

        dto.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task Update_ShouldAddTheFirstRow_AndCommit()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns((PresenceSphereSettings?)null);

        var dto = await UpdateHandler().Handle(new UpdatePresenceSphereSettingsCommand(0.8m, 70, true), CancellationToken.None);

        _settings.Received(1).Add(Arg.Is<PresenceSphereSettings>(s => s.DotSizeMultiplier == 0.8m && s.CreatedBy == "admin-1"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        dto.IsDefault.Should().BeFalse();
        dto.CardFillPercent.Should().Be(70);
    }

    [Fact]
    public async Task Update_ShouldChangeTheExistingRow_RecordingWhoAndWhen()
    {
        var existing = PresenceSphereSettings.Create(1.00m, 75, false, "someone", Now.UtcDateTime.AddDays(-1));
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(existing);

        await UpdateHandler().Handle(new UpdatePresenceSphereSettingsCommand(1.5m, 90, true), CancellationToken.None);

        existing.DotSizeMultiplier.Should().Be(1.5m);
        existing.ModifiedBy.Should().Be("admin-1");
        existing.ModifiedAtUtc.Should().Be(Now.UtcDateTime);
        _settings.DidNotReceive().Add(Arg.Any<PresenceSphereSettings>());
    }

    [Fact]
    public async Task Update_ShouldLogTheOldAndNewValues()
    {
        var logger = Substitute.For<ILogger<UpdatePresenceSphereSettingsCommandHandler>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(PresenceSphereSettings.Create(1.00m, 75, false, "someone", Now.UtcDateTime.AddDays(-1)));

        await UpdateHandler(logger).Handle(new UpdatePresenceSphereSettingsCommand(0.5m, 60, true), CancellationToken.None);

        var logged = logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments()[2]!.ToString())
            .Single();
        logged.Should().Contain("admin-1").And.Contain("1.00").And.Contain("0.5").And.Contain("75").And.Contain("60");
    }

    [Fact]
    public async Task Update_ShouldRefuseAnUnidentifiedCaller()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => UpdateHandler().Handle(new UpdatePresenceSphereSettingsCommand(1.00m, 75, false), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0.24, 75, "dotSizeMultiplier")]
    [InlineData(2.01, 75, "dotSizeMultiplier")]
    [InlineData(1.00, 39, "cardFillPercent")]
    [InlineData(1.00, 96, "cardFillPercent")]
    public void Validator_ShouldNameTheFieldAndTheRange_ForAValueOutsideIt(double dotSize, int fill, string field)
    {
        var result = new UpdatePresenceSphereSettingsCommandValidator()
            .Validate(new UpdatePresenceSphereSettingsCommand((decimal)dotSize, fill, false));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().StartWith(field).And.Contain("between");
    }

    [Theory]
    [InlineData(0.25, 40)]
    [InlineData(2.00, 95)]
    [InlineData(1.00, 75)]
    public void Validator_ShouldAcceptTheValuesAtEachEndAndTheDefaults(double dotSize, int fill)
    {
        new UpdatePresenceSphereSettingsCommandValidator()
            .Validate(new UpdatePresenceSphereSettingsCommand((decimal)dotSize, fill, true))
            .IsValid.Should().BeTrue();
    }
}
