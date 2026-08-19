using AskLucy.Application.Analytics.Commands.RecordFunnelEvent;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Analytics;

public sealed class RecordFunnelEventCommandValidatorTests
{
    private readonly RecordFunnelEventCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_ForAWellFormedCtaClickedEvent()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.TryPlatform, null, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldPass_ForAWellFormedFunnelCompletedEvent()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.FunnelCompleted, null, FunnelKind.SignUp, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenCtaClickedHasNoCtaId()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, null, null, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenCtaClickedAlsoSetsFunnelType()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.SignIn, FunnelKind.SignIn, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenFunnelCompletedHasNoFunnelType()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.FunnelCompleted, null, null, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenFunnelCompletedAlsoSetsCtaId()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.FunnelCompleted, FunnelCtaId.SignUp, FunnelKind.SignUp, Guid.NewGuid(), DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenSessionIdIsEmpty()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.SignIn, null, Guid.Empty, DateTime.UtcNow);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenOccurredAtUtcIsTooFarInTheFuture()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.SignIn, null, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(10));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenOccurredAtUtcIsTooFarInThePast()
    {
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.SignIn, null, Guid.NewGuid(), DateTime.UtcNow.AddHours(-2));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
