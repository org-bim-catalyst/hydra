using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// specs/058-password-recovery T054. The predicate itself lives in the repository and needs a real
/// database to exercise; what is worth pinning here is the retention window the job asks for,
/// because shortening it by accident would quietly destroy the audit trail an incident needs.
/// </summary>
public sealed class PasswordResetTokenCleanupJobTests
{
    private readonly IPasswordResetTokenRepository _resetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly PasswordResetTokenCleanupJob _job;

    public PasswordResetTokenCleanupJobTests() =>
        _job = new PasswordResetTokenCleanupJob(_resetTokens, NullLogger<PasswordResetTokenCleanupJob>.Instance);

    [Fact]
    public async Task RunAsync_ShouldDeleteSpentTokensOlderThanNinetyDays_InABoundedBatch()
    {
        DateTime? cutoff = null;
        var batchSize = 0;

        _resetTokens
            .DeleteSpentBeforeAsync(
                Arg.Do<DateTime>(d => cutoff = d),
                Arg.Do<int>(b => batchSize = b),
                Arg.Any<CancellationToken>())
            .Returns(3);

        await _job.RunAsync(TestContext.Current.CancellationToken);

        cutoff.Should().BeCloseTo(DateTime.UtcNow.AddDays(-90), TimeSpan.FromMinutes(1));
        batchSize.Should().BePositive("an unbounded delete would make one neglected run a table-sized transaction");
    }
}
