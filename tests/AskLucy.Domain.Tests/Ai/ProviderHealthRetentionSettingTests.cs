using AskLucy.Domain.Ai;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

/// <summary>specs/082 — the retention window is a whole number of days in [1, 365].</summary>
public sealed class ProviderHealthRetentionSettingTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProviderHealthRetentionSetting.MinRetentionDays)]
    [InlineData(30)]
    [InlineData(ProviderHealthRetentionSetting.MaxRetentionDays)]
    public void Create_AcceptsEveryValueInsideTheRange(int days)
    {
        var setting = ProviderHealthRetentionSetting.Create(days, "admin-1", Now);

        setting.Id.Should().Be(ProviderHealthRetentionSetting.SingletonId);
        setting.RetentionDays.Should().Be(days);
        setting.CreatedBy.Should().Be("admin-1");
    }

    [Theory]
    [InlineData(ProviderHealthRetentionSetting.MinRetentionDays - 1)]
    [InlineData(ProviderHealthRetentionSetting.MaxRetentionDays + 1)]
    [InlineData(int.MaxValue)]
    public void Update_RejectsAValueOutsideTheRange_ChangingNothing(int days)
    {
        var setting = ProviderHealthRetentionSetting.Create(30, "admin-1", Now);

        var act = () => setting.Update(days, "admin-2", Now.AddMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
        setting.RetentionDays.Should().Be(30);
        setting.ModifiedBy.Should().Be("admin-1");
    }

    [Fact]
    public void Update_StoresTheValueAndTheAuditFields()
    {
        var setting = ProviderHealthRetentionSetting.Create(30, "admin-1", Now);

        setting.Update(14, "admin-2", Now.AddMinutes(5));

        setting.RetentionDays.Should().Be(14);
        setting.ModifiedAtUtc.Should().Be(Now.AddMinutes(5));
        setting.ModifiedBy.Should().Be("admin-2");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void IsValidRetentionDays_MatchesTheInclusiveRange(int days, bool expected) =>
        ProviderHealthRetentionSetting.IsValidRetentionDays(days).Should().Be(expected);

    [Fact]
    public void Defaults_AreThirtyDays() =>
        ProviderHealthRetentionSetting.DefaultRetentionDays.Should().Be(30);
}
