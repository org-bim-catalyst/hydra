using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Ai;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Infrastructure.OperationalFailures;
using AskLucy.Persistence;
using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T061 (SC-003, quickstart S5) — the 2026-09-22 burst: one user, seven ElevenLabs 401s
/// each failed over to Supertonic, each followed by ElevenLabs serving again. Replayed through
/// the real voice router, reporter and recorder into the real ingestor and store, it must be one
/// Critical incident with seven occurrences and seven recoveries — not fourteen rows.
/// </summary>
public sealed class ElevenLabsBurstReplayTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const int Pairs = 7;

    // Unique per run, so the incident never joins one from another run on the shared test database.
    private readonly string _providerName = $"ElevenLabs-{Guid.NewGuid():N}";
    private readonly string _userId = $"burst-user-{Guid.NewGuid():N}";

    [Fact]
    public async Task SevenFailoverRecoveryPairs_ShouldBeOneIncident_WithSevenOccurrencesAndSevenRecoveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorder = new ChannelOperationalFailureRecorder(
            Microsoft.Extensions.Options.Options.Create(new OperationalFailuresOptions()),
            new CorrelationIdAccessor(Substitute.For<IHttpContextAccessor>()),
            TimeProvider.System,
            NullLogger<ChannelOperationalFailureRecorder>.Instance);
        var memory = new VoiceFailoverMemory();
        var elevenLabs = new ToggledEngine("ElevenLabs", _providerName);
        var supertonic = new ToggledEngine("Supertonic", "Supertonic");

        try
        {
            for (var i = 0; i < Pairs; i++)
            {
                elevenLabs.Rejects = true;
                await SpeakAsync(recorder, memory, elevenLabs, supertonic, ct);
                await IngestAsync(recorder, ct);

                elevenLabs.Rejects = false;
                await SpeakAsync(recorder, memory, elevenLabs, supertonic, ct);
                await IngestAsync(recorder, ct);
            }

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            var incident = (await db.OperationalFailureIncidents.AsNoTracking()
                .Where(i => i.ProviderName == _providerName)
                .ToListAsync(ct)).Should().ContainSingle().Subject;

            incident.HighestSeverity.Should().Be(OperationalFailureSeverity.Critical, "a rejected credential is Critical even though Supertonic served the user");
            incident.Engine.Should().Be(OperationalFailureEngine.Voice);
            incident.Kind.Should().Be(OperationalFailureKind.CredentialRejected);
            incident.OccurrenceCount.Should().Be(Pairs);
            incident.RecoveryCount.Should().Be(Pairs);
            incident.DistinctUserCount.Should().Be(1);

            var occurrences = await db.OperationalFailureOccurrences.AsNoTracking()
                .Where(o => o.IncidentId == incident.Id)
                .ToListAsync(ct);
            occurrences.Should().HaveCount(Pairs).And.OnlyContain(o => o.IsFailover);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();

            // Occurrences and participants cascade from the incident.
            await db.OperationalFailureIncidents.Where(i => i.ProviderName == _providerName).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    /// <summary>One request: a fresh (scoped) router and reporter, sharing the singleton recorder and memory.</summary>
    private async Task SpeakAsync(
        IOperationalFailureRecorder recorder, VoiceFailoverMemory memory, ToggledEngine elevenLabs, ToggledEngine supertonic, CancellationToken ct)
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(_userId);
        var voiceProviders = Substitute.For<IVoiceProviderRepository>();
        voiceProviders.ListByPriorityAsync(Arg.Any<CancellationToken>())
            .Returns([VoiceProvider.Create("ElevenLabs", "ElevenLabs", 0, "system"), VoiceProvider.Create("Supertonic", "Supertonic", 1, "system")]);
        var aiProviders = Substitute.For<IAIProviderRepository>();
        aiProviders.ListAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        var router = new VoiceProviderRouter(
            voiceProviders,
            [elevenLabs, supertonic],
            Substitute.For<IAiCredentialProtector>(),
            aiProviders,
            new VoiceFailureReporter(recorder, new FailureClassifier(), memory, currentUser),
            NullLogger<VoiceProviderRouter>.Instance);

        var settings = await router.ResolveDefaultSettingsAsync("en", ct);
        await foreach (var _ in router.StreamSpeechAsync("Hello.", settings, ct))
        {
        }
    }

    /// <summary>Drains the channel into the real ingestor, as the background writer would.</summary>
    private async Task IngestAsync(ChannelOperationalFailureRecorder recorder, CancellationToken ct)
    {
        var signals = new List<OperationalFailureSignal>();
        while (recorder.Reader.TryRead(out var signal))
        {
            signals.Add(signal);
        }

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OperationalFailureIngestor>().IngestAsync(signals, ct);
    }

    private sealed class ToggledEngine(string providerKey, string displayName) : ITextToSpeechEngine
    {
        public bool Rejects { get; set; }

        public string ProviderKey => providerKey;

        public string DisplayName => displayName;

        public bool RequiresCredential => false;

        public Task<IReadOnlyList<VoiceOptionDto>> ListVoicesAsync(string? apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceOptionDto>>([]);

        public VoiceSettingsDto ResolveDefaultSettings(string language, string? voiceId) =>
            new(voiceId ?? "voice", "eleven_flash_v2_5", 0, 0, 0, 1.0, false, "mp3", language, providerKey);

        public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
            string text, VoiceSettingsDto settings, string? apiKey, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (Rejects)
            {
                throw new AiProviderAuthenticationException("The voice provider rejected the credential.");
            }

            yield return [1];
        }
    }
}
