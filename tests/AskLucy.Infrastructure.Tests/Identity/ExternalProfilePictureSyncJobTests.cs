using System.Linq;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Identity;

/// <summary>
/// specs/062-external-login-profile-sync T018-T022. [LoggerMessage] source-gen logging needs
/// IsEnabled stubbed and ReceivedCalls() inspected — a plain Received().Log(...) assertion
/// never matches the generated TState struct (see AskLucy.Infrastructure.Tests.Email.ConsoleEmailSenderTests).
/// </summary>
public sealed class ExternalProfilePictureSyncJobTests
{
    private readonly IRemoteFileDownloader _remoteFileDownloader = Substitute.For<IRemoteFileDownloader>();
    private readonly IImageContentValidator _imageContentValidator = Substitute.For<IImageContentValidator>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IUserProfileRepository _userProfileRepository = Substitute.For<IUserProfileRepository>();
    private readonly ILogger<ExternalProfilePictureSyncJob> _logger = Substitute.For<ILogger<ExternalProfilePictureSyncJob>>();
    private readonly ExternalProfilePictureSyncJob _job;

    public ExternalProfilePictureSyncJobTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _job = new ExternalProfilePictureSyncJob(
            _remoteFileDownloader, _imageContentValidator, _fileStorage, _userProfileRepository, _logger);
    }

    private static MemoryStream ImageBytes() => new(Encoding.UTF8.GetBytes("fake-image-bytes"));

    private List<string> LoggedWarnings() =>
        _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning)
            .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
            .ToList();

    [Fact]
    public async Task SyncAsync_ShouldStoreTheAvatar_WhenTheHostIsAllowListed()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/a/photo");
        _remoteFileDownloader.DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadedFile(ImageBytes(), "image/jpeg"));
        _imageContentValidator.IsValidImage(Arg.Any<Stream>(), out Arg.Any<string?>()).Returns(true);
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "external-profile-picture", Arg.Any<CancellationToken>())
            .Returns("stored-file-name.jpg");

        await _job.SyncAsync("user-1", uri.ToString(), TestContext.Current.CancellationToken);

        await _userProfileRepository.Received(1).SetAvatarFileNameAsync("user-1", "stored-file-name.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_ShouldRejectTheUrl_WhenTheHostIsNotAllowListed()
    {
        await _job.SyncAsync("user-2", "https://evil.example.com/photo.jpg", TestContext.Current.CancellationToken);

        await _remoteFileDownloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default, TestContext.Current.CancellationToken);
        await _userProfileRepository.DidNotReceiveWithAnyArgs().SetAvatarFileNameAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SyncAsync_ShouldNotStoreAnything_WhenTheDownloadExceedsTheSizeCap()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/a/oversized");
        _remoteFileDownloader.DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns<DownloadedFile>(_ => throw new InvalidOperationException("Content exceeds the maximum allowed length."));

        await _job.SyncAsync("user-3", uri.ToString(), TestContext.Current.CancellationToken);

        await _fileStorage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, TestContext.Current.CancellationToken);
        await _userProfileRepository.DidNotReceiveWithAnyArgs().SetAvatarFileNameAsync(default!, default, TestContext.Current.CancellationToken);
        LoggedWarnings().Should().Contain(message => message.Contains("user-3"));
    }

    [Fact]
    public async Task SyncAsync_ShouldNotStoreAnything_WhenTheDownloadFailsContentValidation()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/a/corrupted");
        _remoteFileDownloader.DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadedFile(ImageBytes(), "image/jpeg"));
        _imageContentValidator.IsValidImage(Arg.Any<Stream>(), out Arg.Any<string?>()).Returns(false);

        await _job.SyncAsync("user-4", uri.ToString(), TestContext.Current.CancellationToken);

        await _fileStorage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, TestContext.Current.CancellationToken);
        await _userProfileRepository.DidNotReceiveWithAnyArgs().SetAvatarFileNameAsync(default!, default, TestContext.Current.CancellationToken);
        LoggedWarnings().Should().Contain(message => message.Contains("user-4"));
    }

    [Fact]
    public async Task SyncAsync_ShouldSwallowTheFailure_WhenTheDownloadThrows()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/a/unreachable");
        _remoteFileDownloader.DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns<DownloadedFile>(_ => throw new HttpRequestException("boom"));

        var act = async () => await _job.SyncAsync("user-5", uri.ToString(), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        await _userProfileRepository.DidNotReceiveWithAnyArgs().SetAvatarFileNameAsync(default!, default, TestContext.Current.CancellationToken);
        LoggedWarnings().Should().Contain(message => message.Contains("user-5"));
    }

    [Fact]
    public async Task SyncAsync_ShouldAlwaysFetchAndStore_WithNoComparisonAgainstAPreviouslyStoredValue()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/a/same-photo");
        _remoteFileDownloader.DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new DownloadedFile(ImageBytes(), "image/jpeg"), _ => new DownloadedFile(ImageBytes(), "image/jpeg"));
        _imageContentValidator.IsValidImage(Arg.Any<Stream>(), out Arg.Any<string?>()).Returns(true);
        _fileStorage.SaveAsync(Arg.Any<Stream>(), "external-profile-picture", Arg.Any<CancellationToken>())
            .Returns("stored-file-name.jpg");

        await _job.SyncAsync("user-6", uri.ToString(), TestContext.Current.CancellationToken);
        await _job.SyncAsync("user-6", uri.ToString(), TestContext.Current.CancellationToken);

        await _remoteFileDownloader.Received(2).DownloadAsync(uri, Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await _userProfileRepository.Received(2).SetAvatarFileNameAsync("user-6", "stored-file-name.jpg", Arg.Any<CancellationToken>());
    }
}
