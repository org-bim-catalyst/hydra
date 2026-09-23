using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.CustomModels;

/// <summary>
/// specs/072 T011. The filtered unique indexes, the case-insensitive collations and the
/// forward-only progress <c>UPDATE</c> only exist in real SQL Server, so they are proven here
/// rather than faked in Application tests.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class CustomModelRepositoryTests(PersistenceTestFixture fixture)
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task UpdateProgressAsync_ShouldOnlyMoveForward()
    {
        var model = await SeedAsync(Unique("progress"), m => Transferring(m));
        var ct = TestContext.Current.CancellationToken;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new CustomModelRepository(dbContext);
            await repository.UpdateProgressAsync(model.Id, new CustomModelProgress(500, 1, "b.bin", 10, 20), ct);
            await repository.UpdateProgressAsync(model.Id, new CustomModelProgress(400, 0, "a.bin", 5, 20), ct);
        }

        var reloaded = await LoadAsync(model.Id);
        reloaded!.TransferredBytes.Should().Be(500);
        reloaded.CompletedFileCount.Should().Be(1);
        reloaded.CurrentFilePath.Should().Be("b.bin");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Name_ShouldBeUniqueIgnoringCase_UntilSoftDeleted()
    {
        var name = Unique("Supertonic");
        var first = await SeedAsync(name.ToLowerInvariant(), m => m.Fail(CustomModelFailureKind.SourceNotFound, "x", Now));

        var duplicate = () => SeedAsync(name.ToUpperInvariant());
        (await duplicate.Should().ThrowAsync<DuplicateResourceException>()).Which.Message.Should().Contain("name");

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await new CustomModelRepository(dbContext).NameExistsAsync(name.ToUpperInvariant(), TestContext.Current.CancellationToken)).Should().BeTrue();
        }

        await RemoveAsync(first.Id);

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await new CustomModelRepository(dbContext).NameExistsAsync(name, TestContext.Current.CancellationToken)).Should().BeFalse();
        }

        var again = await SeedAsync(name.ToUpperInvariant());
        again.Name.Should().Be(name.ToUpperInvariant());
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task RepositoryAvailable_ShouldBeUniqueIgnoringCase()
    {
        var owner = Unique("Supertone");
        var first = await SeedAsync(Unique("a"), Completed, repository: $"{owner}/supertonic-3");
        await MakeAvailableAsync(first.Id);
        var second = await SeedAsync(Unique("b"), Completed, repository: $"{owner.ToLowerInvariant()}/SUPERTONIC-3");

        var act = () => MakeAvailableAsync(second.Id);

        (await act.Should().ThrowAsync<DuplicateResourceException>()).Which.Message.Should().Contain("available");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task DestinationInProgress_ShouldBeUniqueIgnoringCase_UntilTheJobIsTerminal()
    {
        var folder = Unique("x");
        var first = await SeedAsync(Unique("a"), destination: $"Models/{folder}");

        var act = () => SeedAsync(Unique("b"), destination: $"models/{folder.ToUpperInvariant()}");
        (await act.Should().ThrowAsync<DuplicateResourceException>()).Which.Message.Should().Contain("destination");

        await UpdateAsync(first.Id, m => m.Fail(CustomModelFailureKind.TargetConnectionLost, "x", Now));

        var again = await SeedAsync(Unique("c"), destination: $"models/{folder.ToUpperInvariant()}");
        again.IsInProgress.Should().BeTrue();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AddOverwrittenFileAsync_ShouldInsertTheRow_AndIncrementTheCount()
    {
        var model = await SeedAsync(Unique("overwrite"), m => Transferring(m));
        var ct = TestContext.Current.CancellationToken;

        await using (var dbContext = fixture.CreateDbContext())
        {
            var repository = new CustomModelRepository(dbContext);
            var tracked = await repository.GetByIdAsync(model.Id, ct);
            await repository.AddOverwrittenFileAsync(tracked!.RecordOverwrite($"{model.Destination}/a.onnx", 123, Now), ct);
            await repository.AddOverwrittenFileAsync(tracked.RecordOverwrite($"{model.Destination}/b.onnx", 456, Now), ct);
        }

        await using var verify = fixture.CreateDbContext();
        var verifyRepository = new CustomModelRepository(verify);
        var (items, total) = await verifyRepository.GetOverwrittenFilesAsync(model.Id, 1, 50, ct);
        total.Should().Be(2);
        items.Select(i => i.RelativePath).Should().Equal($"{model.Destination}/a.onnx", $"{model.Destination}/b.onnx");
        items[0].PreviousSizeBytes.Should().Be(123);
        (await verifyRepository.GetByIdAsync(model.Id, ct))!.OverwrittenFileCount.Should().Be(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetRunSignalAsync_ShouldReflectCancellationAndTerminalState()
    {
        var running = await SeedAsync(Unique("run"), m => m.StartListing(Now));
        var cancelling = await SeedAsync(Unique("cancel"), m =>
        {
            m.StartListing(Now);
            m.RequestCancellation("admin-2", Now);
        });
        var failed = await SeedAsync(Unique("failed"), m => m.Fail(CustomModelFailureKind.Unexpected, "x", Now));
        var ct = TestContext.Current.CancellationToken;

        await using var dbContext = fixture.CreateDbContext();
        var repository = new CustomModelRepository(dbContext);

        (await repository.GetRunSignalAsync(running.Id, ct)).Should().Be(CustomModelRunSignal.Continue);
        (await repository.GetRunSignalAsync(cancelling.Id, ct)).Should().Be(CustomModelRunSignal.CancellationRequested);
        (await repository.GetRunSignalAsync(failed.Id, ct)).Should().Be(CustomModelRunSignal.NoLongerInProgress);
        (await repository.GetRunSignalAsync(Guid.CreateVersion7(), ct)).Should().Be(CustomModelRunSignal.NoLongerInProgress);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FindCompletedForRepositoryAsync_ShouldReturnOnlyNonDeletedCompletedModels_IgnoringCase()
    {
        var owner = Unique("Owner");
        var repositoryId = $"{owner}/model";
        await SeedAsync(Unique("queued"), repository: repositoryId);
        await SeedAsync(Unique("running"), m => Transferring(m), repository: repositoryId);
        var failed = await SeedAsync(Unique("failed"), m => m.Fail(CustomModelFailureKind.Unexpected, "x", Now), repository: repositoryId);
        await SeedAsync(Unique("cancelled"), m => m.RequestCancellation("admin-2", Now), repository: repositoryId);
        await RemoveAsync(failed.Id);
        var completed = await SeedAsync(Unique("completed"), Completed, repository: repositoryId);

        await using var dbContext = fixture.CreateDbContext();
        var found = await new CustomModelRepository(dbContext).FindCompletedForRepositoryAsync(repositoryId.ToUpperInvariant(), TestContext.Current.CancellationToken);

        found.Select(m => m.Id).Should().Equal(completed.Id);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FindActiveJobOverlappingDestinationAsync_ShouldMatchOnSegmentBoundaries_IgnoringCase()
    {
        var folder = Unique("a");
        var active = await SeedAsync(Unique("active"), destination: $"Models/{folder}");
        var ct = TestContext.Current.CancellationToken;

        await using var dbContext = fixture.CreateDbContext();
        var repository = new CustomModelRepository(dbContext);

        (await repository.FindActiveJobOverlappingDestinationAsync($"Models/{folder}", ct))!.Id.Should().Be(active.Id);
        (await repository.FindActiveJobOverlappingDestinationAsync($"models/{folder.ToUpperInvariant()}", ct))!.Id.Should().Be(active.Id);
        (await repository.FindActiveJobOverlappingDestinationAsync($"Models/{folder}/b", ct))!.Id.Should().Be(active.Id);
        (await repository.FindActiveJobOverlappingDestinationAsync("Models", ct))!.Id.Should().Be(active.Id);
        (await repository.FindActiveJobOverlappingDestinationAsync($"Models/{folder}b", ct)).Should().BeNull();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task UpdateAsync_ShouldRetryOnce_WhenAProgressWriteBumpedTheRowVersion()
    {
        var model = await SeedAsync(Unique("retry"), m => Transferring(m));
        var ct = TestContext.Current.CancellationToken;

        await using var dbContext = fixture.CreateDbContext();
        var repository = new CustomModelRepository(dbContext);
        var calls = 0;

        var saved = await repository.UpdateAsync(model.Id, m =>
        {
            calls++;
            if (calls == 1)
            {
                // A progress write from another context lands between the load and the save.
                using var other = fixture.CreateDbContext();
                new CustomModelRepository(other).UpdateProgressAsync(model.Id, new CustomModelProgress(10, 0, "a", 1, 2), ct).GetAwaiter().GetResult();
            }

            m.RequestCancellation("admin-2", Now);
            return true;
        }, ct);

        calls.Should().Be(2);
        saved!.CancellationRequestedAtUtc.Should().Be(Now);
        (await LoadAsync(model.Id))!.TransferredBytes.Should().Be(10);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task UpdateAsync_ShouldSaveNothing_WhenTheChangeNoLongerApplies()
    {
        var model = await SeedAsync(Unique("declined"), m => m.Fail(CustomModelFailureKind.Unexpected, "x", Now));

        await using var dbContext = fixture.CreateDbContext();
        var saved = await new CustomModelRepository(dbContext).UpdateAsync(model.Id, _ => false, TestContext.Current.CancellationToken);

        saved.Should().BeNull();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];

    private static void Transferring(CustomModel model)
    {
        model.StartListing(Now);
        model.BeginTransfer(Sha, 100, 1, long.MaxValue);
    }

    private static void Completed(CustomModel model)
    {
        Transferring(model);
        model.RecordProgress(100, 1, null, null, null);
        model.Complete(Now);
    }

    private async Task<CustomModel> SeedAsync(
        string name,
        Action<CustomModel>? arrange = null,
        string? repository = null,
        string? destination = null)
    {
        HuggingFaceModelSource.TryParse($"https://huggingface.co/{repository ?? "Supertone/supertonic-3"}", out var source, out var sourceError)
            .Should().BeTrue(sourceError);
        DeploymentDestination.TryCreate(destination ?? $"Models/{name}", ["Models"], out var target, out var destinationError)
            .Should().BeTrue(destinationError);

        var model = CustomModel.Create(name, source!, target!, "admin-1");
        arrange?.Invoke(model);

        await using var dbContext = fixture.CreateDbContext();
        await new CustomModelRepository(dbContext).AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private async Task<CustomModel?> LoadAsync(Guid id)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await new CustomModelRepository(dbContext).GetByIdAsync(id, TestContext.Current.CancellationToken);
    }

    private async Task UpdateAsync(Guid id, Action<CustomModel> change)
    {
        await using var dbContext = fixture.CreateDbContext();
        var saved = await new CustomModelRepository(dbContext).UpdateAsync(id, m =>
        {
            change(m);
            return true;
        }, TestContext.Current.CancellationToken);
        saved.Should().NotBeNull();
    }

    private Task MakeAvailableAsync(Guid id) => UpdateAsync(id, m => m.MakeAvailable());

    private Task RemoveAsync(Guid id) => UpdateAsync(id, m => m.Remove("admin-2", Now));
}
