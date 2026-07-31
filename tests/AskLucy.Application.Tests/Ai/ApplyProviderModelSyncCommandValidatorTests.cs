using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/009-selective-model-sync-review T004 — FR-013: a request selecting nothing on either side is rejected server-side as a backstop to FR-008's client-side Confirm-disabled guard.</summary>
public sealed class ApplyProviderModelSyncCommandValidatorTests
{
    private readonly ApplyProviderModelSyncCommandValidator _validator = new();

    private static readonly AIModelCapabilities Capabilities = new(true, true, true, true, false, false, true, false, false);

    [Fact]
    public async Task Validate_ShouldFail_WhenBothAddedAndRemovedFromVendorAreEmpty()
    {
        var command = new ApplyProviderModelSyncCommand(Guid.NewGuid(), [], []);

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Nothing to apply.");
    }

    [Fact]
    public async Task Validate_ShouldPass_WhenAtLeastOneAddedEntryIsPresent()
    {
        var command = new ApplyProviderModelSyncCommand(
            Guid.NewGuid(), [new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities)], []);

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ShouldPass_WhenAtLeastOneRemovedFromVendorEntryIsPresent()
    {
        var command = new ApplyProviderModelSyncCommand(
            Guid.NewGuid(), [], [new RemovedModelDto(Guid.NewGuid(), "gpt-3.5", "GPT-3.5")]);

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ShouldFail_WhenProviderIdIsEmpty()
    {
        var command = new ApplyProviderModelSyncCommand(
            Guid.Empty, [new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities)], []);

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
