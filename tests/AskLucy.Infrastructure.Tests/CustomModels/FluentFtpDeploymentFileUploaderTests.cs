using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Infrastructure.CustomModels.Deployment;
using FluentAssertions;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 T032 (connection settings, paths, upload mode) and T062 (FTP failure mapping), against a fake client.</summary>
public sealed class FluentFtpDeploymentFileUploaderTests
{
    private const string Password = "sentinel-ftp-password-7f3a";

    private static readonly DeploymentTargetSettings Settings = new("ftp.example.test", 21, "deployer", Password, "/hydra/", AllowPlainFtp: false);

    private readonly FakeFtpClientFactory _factory = new();

    [Fact]
    public async Task OpenSessionAsync_UsesExplicitTls_AndNeverTrustsAnyCertificate()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);

        var settings = _factory.Created.Should().ContainSingle().Subject.Settings;
        settings.EncryptionMode.Should().Be(FtpEncryptionMode.Explicit);
        settings.ValidateAnyCertificate.Should().BeFalse();
        settings.ReadTimeoutMilliseconds.Should().Be(60_000);
        settings.DataConnectionReadTimeoutMilliseconds.Should().Be(60_000);
        settings.Host.Should().Be("ftp.example.test");
        _factory.Created[0].ConnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task OpenSessionAsync_AllowPlainFtp_UsesNoEncryption_NeverAuto()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings with { AllowPlainFtp = true }, TestContext.Current.CancellationToken);

        _factory.Created.Should().ContainSingle().Which.Settings.EncryptionMode.Should().Be(FtpEncryptionMode.None);
    }

    [Theory]
    [InlineData(false, FtpEncryptionMode.Explicit)]
    [InlineData(true, FtpEncryptionMode.None)]
    public async Task AsyncFtpClientFacade_AppliesTheSettingsToTheRealClient(bool allowPlainFtp, FtpEncryptionMode expected)
    {
        await using var facade = new AsyncFtpClientFacade(
            FluentFtpDeploymentFileUploader.BuildConnectionSettings(Settings with { AllowPlainFtp = allowPlainFtp }));

        facade.Configuration.EncryptionMode.Should().Be(expected);
        facade.Configuration.ValidateAnyCertificate.Should().BeFalse();
        facade.Configuration.ReadTimeout.Should().Be(60_000);
        facade.Configuration.DataConnectionReadTimeout.Should().Be(60_000);
    }

    [Theory]
    [InlineData(SslPolicyErrors.None, true)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
    public void IsCertificateAcceptable_OnlyWithNoPolicyErrors(SslPolicyErrors errors, bool expected) =>
        AsyncFtpClientFacade.IsCertificateAcceptable(errors).Should().Be(expected);

    [Fact]
    public void ConnectionSettings_ToString_HidesThePassword()
    {
        var text = FluentFtpDeploymentFileUploader.BuildConnectionSettings(Settings).ToString();

        text.Should().NotContain(Password).And.Contain("Password = ***");
        Settings.ToString().Should().NotContain(Password);
    }

    [Theory]
    [InlineData("/hydra", "Models/a/b.onnx", "/hydra/Models/a/b.onnx")]
    [InlineData("/hydra/", "Models/a/b.onnx", "/hydra/Models/a/b.onnx")]
    [InlineData("/hydra//", "/Models/a/b.onnx", "/hydra/Models/a/b.onnx")]
    [InlineData("/", "Models/b.onnx", "/Models/b.onnx")]
    [InlineData("", "Models/b.onnx", "/Models/b.onnx")]
    public void CombineRemotePath_JoinsWithExactlyOneSlash(string root, string relative, string expected) =>
        FluentFtpDeploymentFileUploader.CombineRemotePath(root, relative).Should().Be(expected);

    [Fact]
    public async Task UploadAsync_OverwritesAndCreatesDirectories_UnderTheRootPath_AndReportsBytes()
    {
        var reports = new List<long>();
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        var client = _factory.Created[0];
        client.ProgressToReport = [100, 4096];

        await session.UploadAsync("C:/temp/model.onnx", "Models/supertonic-3/onnx/model.onnx", new SyncProgress(reports), TestContext.Current.CancellationToken);

        var upload = client.Uploads.Should().ContainSingle().Subject;
        upload.Should().Be(new FakeFtpClient.Upload(
            "C:/temp/model.onnx", "/hydra/Models/supertonic-3/onnx/model.onnx", FtpRemoteExists.Overwrite, CreateRemoteDir: true, FtpVerify.None));
        reports.Should().Equal(100, 4096);
    }

    [Fact]
    public async Task GetRemoteFileSizeAsync_ReturnsTheSize_OrNullWhenMissing()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        _factory.Created[0].RemoteSizes["/hydra/Models/x/config.json"] = 42;

        (await session.GetRemoteFileSizeAsync("Models/x/config.json", TestContext.Current.CancellationToken)).Should().Be(42);
        (await session.GetRemoteFileSizeAsync("Models/x/missing.json", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task GetRemoteFileSizeAsync_ConnectionDroppedBetweenFiles_ReconnectsOnce()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        var client = _factory.Created[0];
        client.IsConnected = false;

        await session.GetRemoteFileSizeAsync("Models/x/config.json", TestContext.Current.CancellationToken);

        client.ConnectCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetRemoteFileSizeAsync_ReconnectFails_IsConnectionLost_NamingTheFile()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        var client = _factory.Created[0];
        client.IsConnected = false;
        client.ConnectException = new FtpException("Error while connecting", new SocketException((int)SocketError.ConnectionRefused));

        var act = () => session.GetRemoteFileSizeAsync("Models/x/config.json", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DeploymentTargetException>()).Which;
        thrown.Kind.Should().Be(DeploymentTargetFailureKind.ConnectionLost);
        thrown.Message.Should().Contain("Models/x/config.json");
        AssertSafe(thrown);
    }

    [Fact]
    public async Task UploadAsync_StatusOtherThanSuccess_IsWriteRejected()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        _factory.Created[0].UploadStatus = FtpStatus.Failed;

        var act = () => session.UploadAsync("C:/temp/a.bin", "Models/x/a.bin", new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DeploymentTargetException>()).Which.Kind.Should().Be(DeploymentTargetFailureKind.WriteRejected);
    }

    [Fact]
    public async Task UploadAsync_LocalFileMissing_IsNotBlamedOnTheServer()
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        _factory.Created[0].UploadException = new FileNotFoundException("gone");

        var act = () => session.UploadAsync("C:/temp/a.bin", "Models/x/a.bin", new SyncProgress([]), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task OpenSessionAsync_LoginRejected_IsAuthRejected_AndDisposesTheClient()
    {
        _factory.ConnectException = new FtpAuthenticationException("530", "Login incorrect.");

        var act = () => CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DeploymentTargetException>()).Which;
        thrown.Kind.Should().Be(DeploymentTargetFailureKind.AuthRejected);
        _factory.Created[0].Disposed.Should().BeTrue();
        AssertSafe(thrown);
    }

    public static TheoryData<Exception, bool, DeploymentTargetFailureKind> ConnectFailures => new()
    {
        { new FtpAuthenticationException("530", "Login incorrect."), false, DeploymentTargetFailureKind.AuthRejected },
        { new FtpCommandException("530", "Not logged in."), false, DeploymentTargetFailureKind.AuthRejected },
        { new FtpSecurityNotAvailableException("AUTH TLS was refused: 502 Command not implemented"), false, DeploymentTargetFailureKind.TlsNotAccepted },
        { new AuthenticationException("The remote certificate is invalid according to the validation procedure."), true, DeploymentTargetFailureKind.CertificateInvalid },
        { new FtpException("Error during TLS handshake", new AuthenticationException("The remote certificate was rejected.")), true, DeploymentTargetFailureKind.CertificateInvalid },
        { new FtpInvalidCertificateException("The certificate is invalid."), false, DeploymentTargetFailureKind.CertificateInvalid },
        { new AuthenticationException("Authentication failed because the remote party has closed the transport stream."), false, DeploymentTargetFailureKind.TlsNotAccepted },
        { new TimeoutException("Timed out trying to connect!"), false, DeploymentTargetFailureKind.ConnectionLost },
        { new SocketException((int)SocketError.HostNotFound), false, DeploymentTargetFailureKind.ConnectionLost },
    };

    [Theory]
    [MemberData(nameof(ConnectFailures))]
    public async Task OpenSessionAsync_ConnectFailure_MapsToKind_WithoutThePassword(Exception failure, bool certificateRejected, DeploymentTargetFailureKind expected)
    {
        _factory.ConnectException = failure;
        _factory.CertificateRejected = certificateRejected;

        var act = () => CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DeploymentTargetException>()).Which;
        thrown.Kind.Should().Be(expected);
        thrown.InnerException.Should().BeSameAs(failure);
        AssertSafe(thrown);
    }

    public static TheoryData<Exception, DeploymentTargetFailureKind> UploadFailures => new()
    {
        { new FtpException("Error while uploading the file to the server. See InnerException for more info.", new IOException("Connection reset by peer", new SocketException((int)SocketError.ConnectionReset))), DeploymentTargetFailureKind.ConnectionLost },
        { new IOException("Unable to read data from the transport connection."), DeploymentTargetFailureKind.ConnectionLost },
        { new TimeoutException("Timed out trying to read data from the socket stream!"), DeploymentTargetFailureKind.ConnectionLost },
        { new FtpCommandException("550", "Permission denied."), DeploymentTargetFailureKind.WriteRejected },
        { new FtpException("Error while uploading the file to the server.", new FtpCommandException("553", "Could not create file.")), DeploymentTargetFailureKind.WriteRejected },
    };

    [Theory]
    [MemberData(nameof(UploadFailures))]
    public async Task UploadAsync_Failure_MapsToKind_NamesTheFile_WithoutThePassword(Exception failure, DeploymentTargetFailureKind expected)
    {
        await using var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        _factory.Created[0].UploadException = failure;

        var act = () => session.UploadAsync("C:/temp/model.onnx", "Models/x/onnx/model.onnx", new SyncProgress([]), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DeploymentTargetException>()).Which;
        thrown.Kind.Should().Be(expected);
        thrown.Message.Should().Contain("Models/x/onnx/model.onnx");
        AssertSafe(thrown);
    }

    [Fact]
    public async Task DisposeAsync_DisconnectFailure_IsSwallowedAfterLogging_AndStillDisposes()
    {
        var session = await CreateUploader().OpenSessionAsync(Settings, TestContext.Current.CancellationToken);
        var client = _factory.Created[0];
        client.DisconnectException = new IOException("reset");

        await session.DisposeAsync();

        client.Disposed.Should().BeTrue();
    }

    /// <summary>SC-006: the admin-visible message and the logged <c>ToString()</c> never carry the password, host or root path.</summary>
    private static void AssertSafe(DeploymentTargetException exception)
    {
        exception.ToString().Should().NotContain(Password);
        exception.Message.Should().NotContain(Password).And.NotContain("ftp.example.test").And.NotContain("/hydra");
    }

    private FluentFtpDeploymentFileUploader CreateUploader() =>
        new(_factory, NullLogger<FluentFtpDeploymentFileUploader>.Instance);

    private sealed class SyncProgress(List<long> reports) : IProgress<long>
    {
        public void Report(long value) => reports.Add(value);
    }

    private sealed class FakeFtpClientFactory : IFtpClientFacadeFactory
    {
        public List<FakeFtpClient> Created { get; } = [];

        public Exception? ConnectException { get; set; }

        public bool CertificateRejected { get; set; }

        public IFtpClientFacade Create(FtpConnectionSettings settings)
        {
            var client = new FakeFtpClient(settings) { ConnectException = ConnectException, CertificateRejected = CertificateRejected };
            Created.Add(client);
            return client;
        }
    }

    private sealed class FakeFtpClient(FtpConnectionSettings settings) : IFtpClientFacade
    {
        public sealed record Upload(string LocalPath, string RemotePath, FtpRemoteExists ExistsMode, bool CreateRemoteDir, FtpVerify Verify);

        public FtpConnectionSettings Settings { get; } = settings;

        public bool IsConnected { get; set; }

        public bool CertificateRejected { get; set; }

        public int ConnectCalls { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? ConnectException { get; set; }

        public Exception? DisconnectException { get; set; }

        public Exception? UploadException { get; set; }

        public FtpStatus UploadStatus { get; set; } = FtpStatus.Success;

        public long[] ProgressToReport { get; set; } = [];

        public Dictionary<string, long> RemoteSizes { get; } = new(StringComparer.Ordinal);

        public List<Upload> Uploads { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCalls++;
            if (ConnectException is not null)
            {
                return Task.FromException(ConnectException);
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return DisconnectException is null ? Task.CompletedTask : Task.FromException(DisconnectException);
        }

        public Task<long> GetFileSizeAsync(string remotePath, CancellationToken cancellationToken) =>
            Task.FromResult(RemoteSizes.TryGetValue(remotePath, out var size) ? size : -1);

        public Task<FtpStatus> UploadFileAsync(
            string localPath,
            string remotePath,
            FtpRemoteExists existsMode,
            bool createRemoteDir,
            FtpVerify verifyOptions,
            IProgress<FtpProgress> progress,
            CancellationToken cancellationToken)
        {
            if (UploadException is not null)
            {
                return Task.FromException<FtpStatus>(UploadException);
            }

            foreach (var bytes in ProgressToReport)
            {
                progress.Report(new FtpProgress(0, bytes, 0, TimeSpan.Zero, localPath, remotePath, null));
            }

            Uploads.Add(new Upload(localPath, remotePath, existsMode, createRemoteDir, verifyOptions));
            return Task.FromResult(UploadStatus);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
