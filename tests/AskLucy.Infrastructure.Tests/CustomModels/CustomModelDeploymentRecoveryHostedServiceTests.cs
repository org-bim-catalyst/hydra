using System.Reflection;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Jobs;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using AskLucy.Infrastructure.CustomModels;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 T065 — the startup sweep (research D6, FR-013): <see cref="CustomModelDeploymentRecovery"/> and the hosted service that runs it.</summary>
public sealed class CustomModelDeploymentRecoveryHostedServiceTests : IDisposable
{
    private static readonly DateTime Boot = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly SweepRepository _repository = new();
    private readonly JobStorage _jobStorage = Substitute.For<JobStorage>();
    private readonly IStorageConnection _connection = Substitute.For<IStorageConnection>();
    private readonly SweepTempStorage _tempStorage = new();
    private readonly FakeLogger<CustomModelDeploymentRecovery> _logger = new();
    private readonly ServiceProvider _services;

    public CustomModelDeploymentRecoveryHostedServiceTests()
    {
        _jobStorage.GetConnection().Returns(_connection);
        _services = new ServiceCollection().AddSingleton<ICustomModelRepository>(_repository).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _tempStorage.Dispose();
    }

    [Theory]
    [InlineData(CustomModelDeploymentState.Listing)]
    [InlineData(CustomModelDeploymentState.Transferring)]
    public async Task Run_RunningRecord_FailsAsInterrupted_AndDeletesItsTempFolder(CustomModelDeploymentState state)
    {
        var model = Seed(state);
        _tempStorage.PrepareJobDirectory(model.Id);

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        model.FailureReason.Should().Be(CustomModelDeploymentRecovery.InterruptedReason);
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeFalse();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "Failed");
    }

    [Theory]
    [InlineData("Enqueued")]
    [InlineData("Scheduled")]
    [InlineData("Processing")]
    public async Task Run_QueuedWithALiveJob_IsLeftAlone(string jobState)
    {
        var model = Seed(CustomModelDeploymentState.Queued, jobId: "job-1");
        _connection.GetStateData("job-1").Returns(new StateData { Name = jobState });
        _tempStorage.PrepareJobDirectory(model.Id);

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Queued);
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("job-1", null)]
    [InlineData("job-1", "Failed")]
    [InlineData("job-1", "Succeeded")]
    [InlineData("job-1", "Deleted")]
    public async Task Run_QueuedWithNoLiveJob_FailsAsInterrupted(string? jobId, string? jobState)
    {
        var model = Seed(CustomModelDeploymentState.Queued, jobId);
        _connection.GetStateData("job-1").Returns(jobState is null ? null : new StateData { Name = jobState });

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
        model.FailureReason.Should().Be(CustomModelDeploymentRecovery.LostJobReason);
    }

    [Fact]
    public async Task Run_HangfireUnreadable_LeavesTheQueuedRecord_AndLogsIt()
    {
        var model = Seed(CustomModelDeploymentState.Queued, jobId: "job-1");
        _connection.GetStateData("job-1").Throws(new InvalidOperationException("storage down"));

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Queued);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "LogJobStateUnreadable" && r.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Run_RecordCreatedSinceBoot_IsLeftAlone()
    {
        var model = Seed(CustomModelDeploymentState.Queued);
        model.CreatedAtUtc = Boot.AddSeconds(1);
        _tempStorage.PrepareJobDirectory(model.Id);

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Queued);
        Directory.Exists(_tempStorage.GetJobDirectory(model.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Run_OneConflict_ReloadsAndRetries()
    {
        var model = Seed(CustomModelDeploymentState.Transferring);
        _repository.UpdateConflicts = 1;

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        _repository.UpdateApplyCount.Should().Be(2);
        model.FailureKind.Should().Be(CustomModelFailureKind.InterruptedByRestart);
    }

    [Fact]
    public async Task Run_ConflictWithTheJobFinishing_ReEvaluatesAndLeavesItCompleted()
    {
        var model = Seed(CustomModelDeploymentState.Transferring);
        _repository.UpdateConflicts = 1;
        _repository.OnConflict = m =>
        {
            m.RecordProgress(m.TotalBytes!.Value, m.TotalFileCount!.Value, null, null, null);
            m.Complete(Boot);
        };

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        model.DeploymentState.Should().Be(CustomModelDeploymentState.Completed);
        _logger.Collector.GetSnapshot().Should().NotContain(r => r.Id.Name == "Failed");
    }

    [Fact]
    public async Task Run_SecondConflict_IsLogged_AndTheOtherRecordsAreStillSwept()
    {
        var stuck = Seed(CustomModelDeploymentState.Transferring);
        var other = Seed(CustomModelDeploymentState.Listing);
        _repository.UpdateConflicts = 2;

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        stuck.IsInProgress.Should().BeTrue();
        other.DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "LogRecordFailFailed" && r.Exception is ConcurrencyConflictException);
    }

    [Fact]
    public async Task Run_DeletesTempFoldersWithNoInProgressRecord()
    {
        var orphan = Guid.NewGuid();
        _tempStorage.PrepareJobDirectory(orphan);
        var finished = Seed(CustomModelDeploymentState.Queued);
        finished.RequestCancellation("admin-1", Boot);
        _tempStorage.PrepareJobDirectory(finished.Id);

        await CreateRecovery().RunAsync(Boot, TestContext.Current.CancellationToken);

        _tempStorage.ListJobDirectories().Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "LogSweepCompleted" && r.Message.Contains("deleted 2 temp folder"));
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryUnreachable_LogsInsteadOfStoppingTheHost()
    {
        _repository.ListException = new InvalidOperationException("database down");
        var hostLogger = new FakeLogger<CustomModelDeploymentRecoveryHostedService>();
        using var service = new CustomModelDeploymentRecoveryHostedService(CreateRecovery(), new FakeTimeProvider(new DateTimeOffset(Boot)), hostLogger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!;

        hostLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "LogSweepFailed" && r.Exception is InvalidOperationException);
    }

    private CustomModel Seed(CustomModelDeploymentState state, string? jobId = null)
    {
        HuggingFaceModelSource.TryParse("https://huggingface.co/Supertone/supertonic-3", out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate($"Models/m{_repository.Models.Count}", CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var destination, out _).Should().BeTrue();
        var model = CustomModel.Create($"model-{_repository.Models.Count}", source!, destination!, "admin-1");
        model.CreatedAtUtc = Boot.AddHours(-1);
        if (jobId is not null)
        {
            model.AssignBackgroundJob(jobId);
        }

        if (state is CustomModelDeploymentState.Listing or CustomModelDeploymentState.Transferring)
        {
            model.StartListing(Boot.AddMinutes(-50));
        }

        if (state == CustomModelDeploymentState.Transferring)
        {
            model.BeginTransfer("0123456789abcdef0123456789abcdef01234567", 10, 1, long.MaxValue);
        }

        _repository.Models.Add(model);
        return model;
    }

    private CustomModelDeploymentRecovery CreateRecovery() =>
        new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _jobStorage,
            _tempStorage,
            new FakeTimeProvider(new DateTimeOffset(Boot)),
            _logger);

    /// <summary>Just what the sweep uses. A conflicting attempt applies to a copy, so its changes are lost as a reload would lose them.</summary>
    private sealed class SweepRepository : ICustomModelRepository
    {
        public List<CustomModel> Models { get; } = [];

        public int UpdateConflicts { get; set; }

        public Action<CustomModel>? OnConflict { get; set; }

        public int UpdateApplyCount { get; private set; }

        public Exception? ListException { get; set; }

        public Task<IReadOnlyList<CustomModel>> ListInProgressAsync(CancellationToken cancellationToken = default) =>
            ListException is not null
                ? Task.FromException<IReadOnlyList<CustomModel>>(ListException)
                : Task.FromResult<IReadOnlyList<CustomModel>>(Models.Where(m => m.IsInProgress).ToList());

        public Task<CustomModel?> UpdateAsync(Guid id, Func<CustomModel, bool> apply, CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                var model = Models.Single(m => m.Id == id);
                UpdateApplyCount++;
                if (UpdateConflicts == 0)
                {
                    return Task.FromResult(apply(model) ? model : null);
                }

                if (!apply(Copy(model)))
                {
                    return Task.FromResult<CustomModel?>(null);
                }

                UpdateConflicts--;
                OnConflict?.Invoke(model);
                if (attempt == 2)
                {
                    throw new ConcurrencyConflictException("The custom model was changed by another request.");
                }
            }
        }

        private static CustomModel Copy(CustomModel model) =>
            (CustomModel)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null)!;

        public Task AddAsync(CustomModel model, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(IReadOnlyList<CustomModel> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(IReadOnlyList<CustomModelOverwrittenFile> Items, int TotalCount)> GetOverwrittenFilesAsync(Guid customModelId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomModel?> FindActiveJobOverlappingDestinationAsync(string destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomModel?> FindAvailableForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomModel>> FindCompletedForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateProgressAsync(Guid id, CustomModelProgress progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddOverwrittenFileAsync(CustomModelOverwrittenFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomModelRunSignal> GetRunSignalAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SweepTempStorage : ICustomModelTempStorage, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "asklucy-custom-model-sweep-tests", Guid.NewGuid().ToString("N"));

        public string GetJobDirectory(Guid customModelId) => Path.Combine(_root, customModelId.ToString("N"));

        public string PrepareJobDirectory(Guid customModelId) => Directory.CreateDirectory(GetJobDirectory(customModelId)).FullName;

        public long? GetAvailableFreeSpace(Guid customModelId) => null;

        public void DeleteJobDirectory(Guid customModelId)
        {
            if (Directory.Exists(GetJobDirectory(customModelId)))
            {
                Directory.Delete(GetJobDirectory(customModelId), recursive: true);
            }
        }

        public IReadOnlyList<Guid> ListJobDirectories() =>
            Directory.Exists(_root) ? Directory.EnumerateDirectories(_root).Select(d => Guid.ParseExact(Path.GetFileName(d), "N")).ToList() : [];

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
