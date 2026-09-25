using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Infrastructure.OperationalFailures;
using AskLucy.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T036 — the real host wires the recorder, the queue and the writer together: one
/// channel shared by both registrations, and a recorded failure reaching the database without
/// anyone awaiting it.
/// </summary>
public sealed class OperationalFailureHostTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public void Host_ShouldResolveTheRecorderAndTheQueue_AsTheSameChannel()
    {
        var recorder = factory.Services.GetRequiredService<IOperationalFailureRecorder>();
        var queue = factory.Services.GetRequiredService<IOperationalFailureQueue>();

        recorder.Should().BeOfType<ChannelOperationalFailureRecorder>();
        Assert.Same(recorder, queue);
    }

    [Fact]
    public async Task Record_ShouldAppearAsAnIncident_WithinFiveSeconds()
    {
        // The store commits a new incident before joining the first occurrence to it (research D3),
        // so the poll waits for the join rather than the bare row.
        // A subject of its own makes the grouping key unique, so this never joins another run's incident.
        var subject = new OperationalFailureSubject("HostSmokeTest", Guid.NewGuid());
        var report = new OperationalFailureReport
        {
            Engine = OperationalFailureEngine.Voice,
            Operation = "Text-to-speech",
            Kind = OperationalFailureKind.CredentialRejected,
            Reason = "The provider rejected the credential.",
            ProviderName = "ElevenLabs",
            Subject = subject,
        };
        var groupingKey = OperationalFailureKeys.Grouping(report);

        factory.Services.GetRequiredService<IOperationalFailureRecorder>().Record(report);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            OperationalFailureIncident? incident = null;
            while (incident is null)
            {
                DateTime.UtcNow.Should().BeBefore(deadline, "the writer should have stored the incident by now");
                await Task.Delay(100, TestContext.Current.CancellationToken);
                incident = await QueryAsync(db => db.OperationalFailureIncidents.AsNoTracking()
                    .SingleOrDefaultAsync(i => i.GroupingKey == groupingKey && i.OccurrenceCount > 0, TestContext.Current.CancellationToken));
            }

            incident.OccurrenceCount.Should().Be(1);
            incident.Engine.Should().Be(OperationalFailureEngine.Voice);
        }
        finally
        {
            await QueryAsync(db => db.OperationalFailureIncidents
                .Where(i => i.GroupingKey == groupingKey)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken));
        }
    }

    private async Task<T> QueryAsync<T>(Func<AskLucyDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AskLucyDbContext>());
    }
}
