using System.Net.Security;
using FluentFTP;
using FluentFTP.Client.BaseClient;

namespace AskLucy.Infrastructure.CustomModels.Deployment;

/// <summary>
/// specs/072 research D2. One <see cref="AsyncFtpClient"/>. No FluentFTP logger is attached: its
/// verbose log includes the <c>PASS</c> command.
/// </summary>
internal sealed class AsyncFtpClientFacade : IFtpClientFacade
{
    private readonly AsyncFtpClient client;

    public AsyncFtpClientFacade(FtpConnectionSettings settings)
    {
        client = new AsyncFtpClient(settings.Host, settings.Username, settings.Password, settings.Port);
        client.Config.EncryptionMode = settings.EncryptionMode;
        client.Config.ValidateAnyCertificate = settings.ValidateAnyCertificate;
        client.Config.ReadTimeout = settings.ReadTimeoutMilliseconds;
        client.Config.DataConnectionReadTimeout = settings.DataConnectionReadTimeoutMilliseconds;
        client.ValidateCertificate += OnValidateCertificate;
    }

    public bool IsConnected => client.IsConnected;

    public bool CertificateRejected { get; private set; }

    /// <summary>The applied configuration, so tests can prove the TLS settings reached the client.</summary>
    internal FtpConfig Configuration => client.Config;

    /// <summary>Only a certificate with no policy errors at all is trusted.</summary>
    internal static bool IsCertificateAcceptable(SslPolicyErrors errors) => errors == SslPolicyErrors.None;

    public Task ConnectAsync(CancellationToken cancellationToken) => client.Connect(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) => client.Disconnect(cancellationToken);

    public Task<long> GetFileSizeAsync(string remotePath, CancellationToken cancellationToken) =>
        client.GetFileSize(remotePath, -1, cancellationToken);

    public Task<FtpStatus> UploadFileAsync(
        string localPath,
        string remotePath,
        FtpRemoteExists existsMode,
        bool createRemoteDir,
        FtpVerify verifyOptions,
        IProgress<FtpProgress> progress,
        CancellationToken cancellationToken) =>
        client.UploadFile(localPath, remotePath, existsMode, createRemoteDir, verifyOptions, progress, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        client.ValidateCertificate -= OnValidateCertificate;
        await client.DisposeAsync();
    }

    private void OnValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
    {
        e.Accept = IsCertificateAcceptable(e.PolicyErrors);
        if (!e.Accept)
        {
            CertificateRejected = true;
        }
    }
}

internal sealed class AsyncFtpClientFacadeFactory : IFtpClientFacadeFactory
{
    public IFtpClientFacade Create(FtpConnectionSettings settings) => new AsyncFtpClientFacade(settings);
}
