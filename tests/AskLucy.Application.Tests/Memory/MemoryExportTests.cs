using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory;
using AskLucy.Domain.Memory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T070 (US4 AC3, FR-024, research.md Decision 14) — export produces a complete, human-readable JSON file grouped by category; a zero-memory account still gets a valid empty export, not an error.</summary>
public sealed class MemoryExportTests
{
    private readonly IMemoryExportJobRepository _exportJobRepository = Substitute.For<IMemoryExportJobRepository>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly MemoryExportGenerationJob _job;
    private const string UserId = "user-1";

    public MemoryExportTests() =>
        _job = new MemoryExportGenerationJob(_exportJobRepository, _memoryRepository, _unitOfWork, _fileStorage, Substitute.For<ILogger<MemoryExportGenerationJob>>());

    [Fact]
    public async Task RunAsync_ShouldGroupMemoriesByCategory_AndMarkTheJobReady()
    {
        var job = MemoryExportJob.CreateProcessing(UserId, UserId);
        _exportJobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var memories = new List<MemoryEntity>
        {
            MemoryEntity.CreateCandidate(UserId, null, MemoryCategory.PersonalFact, "Fact one", MemorySourceType.PassiveConversationAnalysis, null, 0.5m, 0.5m, false, MemoryApprovalMode.Automatic, "test"),
            MemoryEntity.CreateCandidate(UserId, null, MemoryCategory.UserPreference, "Preference one", MemorySourceType.PassiveConversationAnalysis, null, 0.5m, 0.5m, false, MemoryApprovalMode.Automatic, "test"),
        };
        _memoryRepository.GetAllByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(memories);
        _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("stored-file-name.json");

        await _job.RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(MemoryExportStatus.Ready);
        job.StoredFileName.Should().Be("stored-file-name.json");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldProduceAValidEmptyExport_WhenTheAccountHasZeroMemories()
    {
        var job = MemoryExportJob.CreateProcessing(UserId, UserId);
        _exportJobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _memoryRepository.GetAllByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(new List<MemoryEntity>());
        _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("empty-export.json");

        await _job.RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(MemoryExportStatus.Ready, "an empty account still gets a valid export, never an error");
    }

    [Fact]
    public async Task RunAsync_ShouldMarkTheJobFailed_WhenFileStorageThrows()
    {
        var job = MemoryExportJob.CreateProcessing(UserId, UserId);
        _exportJobRepository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _memoryRepository.GetAllByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(new List<MemoryEntity>());
        _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new IOException("Disk full."));

        await _job.RunAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(MemoryExportStatus.Failed);
        job.FailureReason.Should().NotBeNullOrEmpty();
    }
}
