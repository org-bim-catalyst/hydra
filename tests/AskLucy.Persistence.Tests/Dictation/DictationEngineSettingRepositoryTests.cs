using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.CustomModels;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Dictation;

/// <summary>
/// specs/078 T008. The singleton row is created on first read (once, even under concurrent first
/// reads), its enums round-trip as strings, and <c>CustomModels.SourceFilePath</c> persists.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class DictationEngineSettingRepositoryTests(PersistenceTestFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetOrCreateAsync_ShouldCreateTheDefaultRowOnce()
    {
        await DeleteSettingAsync();
        var ct = TestContext.Current.CancellationToken;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var created = await new DictationEngineSettingRepository(dbContext).GetOrCreateAsync(ct);
            created.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
            created.PushToTalkEngine.Should().Be(DictationClipEngine.LocalWhisper);
            created.State.Should().Be(DictationEngineState.Active);
            created.LocalWhisperModelId.Should().BeNull();
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            await new DictationEngineSettingRepository(dbContext).GetOrCreateAsync(ct);
        }

        (await CountAsync()).Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetOrCreateAsync_ShouldResolveConcurrentFirstReads_ToOneRow()
    {
        await DeleteSettingAsync();

        var reads = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var dbContext = fixture.CreateDbContext();
            return (await new DictationEngineSettingRepository(dbContext).GetOrCreateAsync(TestContext.Current.CancellationToken)).Id;
        });

        (await Task.WhenAll(reads)).Should().AllBeEquivalentTo(DictationEngineSetting.SingletonId);
        (await CountAsync()).Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Changes_ShouldRoundTrip_AndTheSelectedModelIdIsReadableWithoutTracking()
    {
        await DeleteSettingAsync();
        var ct = TestContext.Current.CancellationToken;
        var modelId = Guid.NewGuid();

        await using (var dbContext = fixture.CreateDbContext())
        {
            var setting = await new DictationEngineSettingRepository(dbContext).GetOrCreateAsync(ct);
            setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
            setting.SetPushToTalkEngine(DictationClipEngine.Browser, "admin-1", Now);
            setting.SelectLocalWhisperModel(modelId, "admin-1", Now);
            setting.Suspend(DictationPrimaryEngine.ElevenLabsRealtime, "Quota exceeded.", Now);
            await dbContext.SaveChangesAsync(ct);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new DictationEngineSettingRepository(dbContext);
            (await repository.GetSelectedLocalWhisperModelIdAsync(ct)).Should().Be(modelId);

            var reloaded = await repository.GetOrCreateAsync(ct);
            reloaded.PrimaryEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
            reloaded.PushToTalkEngine.Should().Be(DictationClipEngine.Browser);
            reloaded.State.Should().Be(DictationEngineState.Suspended);
            reloaded.SuspendedEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
            reloaded.SuspensionReason.Should().Be("Quota exceeded.");
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetSelectedLocalWhisperModelIdAsync_ShouldReturnNull_WithoutCreatingTheRow()
    {
        await DeleteSettingAsync();

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await new DictationEngineSettingRepository(dbContext).GetSelectedLocalWhisperModelIdAsync(TestContext.Current.CancellationToken))
                .Should().BeNull();
        }

        (await CountAsync()).Should().Be(0);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task CustomModelSourceFilePath_ShouldRoundTrip()
    {
        var name = $"whisper-{Guid.NewGuid():N}"[..20];
        HuggingFaceModelSource.TryParse("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate($"Models/{name}", ["Models"], out var target, out _).Should().BeTrue();
        var model = CustomModel.Create(name, source!, target!, "admin-1");
        var ct = TestContext.Current.CancellationToken;

        await using (var dbContext = fixture.CreateDbContext())
        {
            await new CustomModelRepository(dbContext).AddAsync(model, ct);
        }

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await new CustomModelRepository(dbContext).GetByIdAsync(model.Id, ct))!.SourceFilePath.Should().Be("ggml-base.bin");
        }
    }

    private async Task DeleteSettingAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        await dbContext.DictationEngineSettings.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.DictationEngineSettings.CountAsync(TestContext.Current.CancellationToken);
    }
}
