using System.ComponentModel.DataAnnotations;
using AskLucy.Application.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>
/// specs/044-location-viewer-regression T004 — the aggregate boundary budget (FR-003) and its
/// cross-field invariant against <see cref="BoundaryScoringOptions.VisionTimeoutSeconds"/>.
/// A budget smaller than the vision call it contains would silently disable AI vision in
/// production rather than fail loudly, which is exactly the quiet degradation constitution
/// §VIII forbids — so it must be rejected at startup.
/// </summary>
public sealed class BoundaryScoringOptionsTests
{
    private static List<ValidationResult> Validate(BoundaryScoringOptions options) =>
        options.Validate(new ValidationContext(options)).ToList();

    [Fact]
    public void Defaults_ShouldBeValid()
    {
        var options = new BoundaryScoringOptions();

        options.BoundaryTimeoutSeconds.Should().Be(45);
        options.VisionTimeoutSeconds.Should().Be(30);
        Validate(options).Should().BeEmpty();
    }

    [Theory]
    [InlineData(30, 30)] // equal — vision would have exactly zero slack
    [InlineData(20, 30)] // inverted — vision can never finish inside the budget
    [InlineData(1, 30)]
    public void Validate_ShouldFail_WhenBoundaryBudgetDoesNotExceedVisionBudget(int boundaryTimeout, int visionTimeout)
    {
        var options = new BoundaryScoringOptions
        {
            BoundaryTimeoutSeconds = boundaryTimeout,
            VisionTimeoutSeconds = visionTimeout,
        };

        var results = Validate(options);

        results.Should().ContainSingle(r =>
            r.ErrorMessage!.Contains(nameof(BoundaryScoringOptions.BoundaryTimeoutSeconds))
            && r.ErrorMessage.Contains(nameof(BoundaryScoringOptions.VisionTimeoutSeconds)));
        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(BoundaryScoringOptions.BoundaryTimeoutSeconds))
            && r.MemberNames.Contains(nameof(BoundaryScoringOptions.VisionTimeoutSeconds)));
    }

    [Theory]
    [InlineData(45, 30)]
    [InlineData(31, 30)]
    [InlineData(300, 1)]
    public void Validate_ShouldPass_WhenBoundaryBudgetExceedsVisionBudget(int boundaryTimeout, int visionTimeout)
    {
        var options = new BoundaryScoringOptions
        {
            BoundaryTimeoutSeconds = boundaryTimeout,
            VisionTimeoutSeconds = visionTimeout,
        };

        Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldStillEnforceTheExistingWeightAndThresholdInvariants()
    {
        // Guards against the new rule being added in a way that short-circuits the existing ones.
        var options = new BoundaryScoringOptions
        {
            SourceReliabilityWeight = 0.5,
            HighConfidenceThreshold = 0.5,
            MediumConfidenceThreshold = 0.6,
        };

        var results = Validate(options);

        results.Should().Contain(r => r.ErrorMessage!.Contains("weights must sum to 1.0"));
        results.Should().Contain(r => r.ErrorMessage!.Contains(nameof(BoundaryScoringOptions.HighConfidenceThreshold)));
    }
}
