using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.SystemAgents;
using AskLucy.Domain.Agents;
using AskLucy.Infrastructure.Conversations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Conversations;

/// <summary>
/// specs/045 T109, contracts/system-agent-provisioning.md §4's testing table — exercised against
/// a small hand-rolled in-memory <see cref="IAgentRepository"/>/<see cref="IUnitOfWork"/> pair
/// rather than a real database, since <see cref="SystemAgentProvisioner"/>'s own logic is a pure
/// function of what those two interfaces report; no SQL-specific behaviour is under test here
/// (the SQL Server error-number detection <see cref="IUnitOfWork.TrySaveChangesAsync"/> itself
/// performs is Persistence's own concern, not this class's).
/// </summary>
public sealed class SystemAgentProvisionerTests
{
    private readonly IDatabaseMigrationStatus _migrationStatus = Substitute.For<IDatabaseMigrationStatus>();

    public SystemAgentProvisionerTests() =>
        _migrationStatus.HasPendingMigrationsAsync(Arg.Any<CancellationToken>()).Returns(false);

    private SystemAgentProvisioner BuildProvisioner(InMemoryAgentRepository repository, IUnitOfWork? unitOfWork = null) =>
        new(repository, unitOfWork ?? new InMemoryUnitOfWork(repository), _migrationStatus, NullLogger<SystemAgentProvisioner>.Instance);

    [Fact]
    public async Task ProvisionAsync_ShouldCreateAllFive_OnAFreshDatabase()
    {
        var repository = new InMemoryAgentRepository();
        var result = await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        result.Should().Be(new SystemAgentProvisioningResult(5, 0, 0, Deferred: false));
        repository.All.Should().HaveCount(5);
        repository.All.Should().OnlyContain(a => a.IsSystemOwned && a.ModelProviderId == null && a.ModelId == null);
        repository.All.Should().OnlyContain(a => a.Versions.Count == 1 && a.PublishedVersionNumber == 1);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldWriteNothing_OnASecondRunWithNoDefinitionChange()
    {
        var repository = new InMemoryAgentRepository();
        await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        var second = await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        second.Should().Be(new SystemAgentProvisioningResult(0, 0, 5, Deferred: false));
        repository.All.Should().HaveCount(5);
        repository.All.Should().OnlyContain(a => a.Versions.Count == 1, "nothing should have republished");
    }

    [Fact]
    public async Task ProvisionAsync_ShouldPublishExactlyOneNewVersion_WhenADefinitionChanges_KeepingThePriorVersionIntact()
    {
        var repository = new InMemoryAgentRepository();
        await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        var orchestrator = repository.All.Single(a => a.SystemKey == "lucy.orchestrator");
        var originalFirstVersion = orchestrator.Versions.Single();

        // Simulate a definition change by publishing a differently-hashed version directly on the
        // already-provisioned agent, the same way a real code change would leave the newest
        // version's hash out of step with SystemAgentDefinitions.All's current ComputeHash().
        orchestrator.UpdateSystemDefinition(
            orchestrator.Name, "a changed description", orchestrator.Instructions, orchestrator.ModelCapability!.Value, orchestrator.ExecutionPolicy, "test-setup");
        orchestrator.PublishSystemVersion([], "a-hash-that-will-never-match-again", "test-setup");

        var result = await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        result.Upgraded.Should().Be(1);
        result.Unchanged.Should().Be(4);
        orchestrator.Versions.Should().HaveCount(3, "the original, the test's own simulated change, and this pass's real re-publish");
        orchestrator.Versions.Should().Contain(v => v.Id == originalFirstVersion.Id, "the original version is never removed (FR-035)");
    }

    [Fact]
    public async Task ProvisionAsync_ShouldDeferWithoutWriting_WhenMigrationsArePending()
    {
        _migrationStatus.HasPendingMigrationsAsync(Arg.Any<CancellationToken>()).Returns(true);
        var repository = new InMemoryAgentRepository();

        var result = await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        result.Deferred.Should().BeTrue();
        repository.All.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_ShouldDeferWithoutThrowing_WhenTheDatabaseIsUnreachable()
    {
        _migrationStatus.HasPendingMigrationsAsync(Arg.Any<CancellationToken>()).Returns<Task<bool>>(_ => throw new TimeoutException("connection timed out"));
        var repository = new InMemoryAgentRepository();

        var act = async () => await BuildProvisioner(repository).ProvisionAsync(CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Deferred.Should().BeTrue();
    }

    [Fact]
    public async Task ProvisionAsync_ShouldRecoverFromALosingCreationRace_WithoutDuplicatingTheSystemKey()
    {
        var repository = new InMemoryAgentRepository();
        var unitOfWork = new ConflictSimulatingUnitOfWork(repository, conflictForSystemKey: "lucy.orchestrator");

        var result = await BuildProvisioner(repository, unitOfWork).ProvisionAsync(CancellationToken.None);

        // The other instance's row is exactly as valid as this one's own attempt would have been —
        // its hash matches the current definition, so this pass reports it as Unchanged, never as
        // a second Created.
        result.Created.Should().Be(4);
        result.Unchanged.Should().Be(1);
        repository.All.Count(a => a.SystemKey == "lucy.orchestrator").Should().Be(1);
    }

    /// <summary>A minimal, stateful in-memory stand-in for the columns <see cref="SystemAgentProvisioner"/> actually reads/writes — every other member is unused by it and throws if called.</summary>
    private sealed class InMemoryAgentRepository : IAgentRepository
    {
        private readonly List<Agent> _agents = [];

        public IReadOnlyList<Agent> All => _agents;

        public Agent? PendingAdd { get; private set; }

        public Task<Agent?> GetBySystemKeyAsync(string systemKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_agents.FirstOrDefault(a => a.SystemKey == systemKey));

        public void Add(Agent agent) => PendingAdd = agent;

        public void CommitPendingAdd()
        {
            if (PendingAdd is not null)
            {
                _agents.Add(PendingAdd);
                PendingAdd = null;
            }
        }

        public void DiscardPendingAdd() => PendingAdd = null;

        public Task<Agent?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(IReadOnlyList<Agent> Items, string? NextCursor)> ListByOwnerAsync(
            string ownerId, AgentStatus? status, AgentType? agentType, string? cursor, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentVersion?> GetVersionAsync(Guid agentId, int versionNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentVersion>> ListVersionsAsync(Guid agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private class InMemoryUnitOfWork(InMemoryAgentRepository repository) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            repository.CommitPendingAdd();
            return Task.FromResult(1);
        }

        public virtual Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default)
        {
            repository.CommitPendingAdd();
            return Task.FromResult(true);
        }
    }

    /// <summary>Simulates exactly one losing race: the first attempted create for the given system key reports failure and seeds the "winner" a concurrent instance would have committed instead.</summary>
    private sealed class ConflictSimulatingUnitOfWork : InMemoryUnitOfWork
    {
        private readonly InMemoryAgentRepository _repository;
        private readonly string _conflictForSystemKey;
        private bool _conflictAlreadySimulated;

        public ConflictSimulatingUnitOfWork(InMemoryAgentRepository repository, string conflictForSystemKey)
            : base(repository)
        {
            _repository = repository;
            _conflictForSystemKey = conflictForSystemKey;
        }

        public override Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default)
        {
            if (!_conflictAlreadySimulated && _repository.PendingAdd?.SystemKey == _conflictForSystemKey)
            {
                _conflictAlreadySimulated = true;
                var winner = Agent.CreateSystemProvisioned(
                    _conflictForSystemKey, _repository.PendingAdd.Name, _repository.PendingAdd.Description, _repository.PendingAdd.AgentType,
                    _repository.PendingAdd.Instructions, _repository.PendingAdd.ModelCapability!.Value, _repository.PendingAdd.ExecutionPolicy,
                    "system:other-instance");
                var definition = SystemAgentDefinitions.All.First(d => d.SystemKey == _conflictForSystemKey);
                winner.PublishSystemVersion(definition.CapabilityKeys, definition.ComputeHash(), "system:other-instance");

                _repository.DiscardPendingAdd();
                _repository.Add(winner);
                _repository.CommitPendingAdd();
                return Task.FromResult(false);
            }

            return base.TrySaveChangesAsync(uniqueIndexNameOnConflict, cancellationToken);
        }
    }
}
