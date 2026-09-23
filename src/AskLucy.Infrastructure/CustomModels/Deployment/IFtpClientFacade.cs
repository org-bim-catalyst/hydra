using System.Text;
using FluentFTP;

namespace AskLucy.Infrastructure.CustomModels.Deployment;

/// <summary>
/// specs/072 research D2. The few <see cref="AsyncFtpClient"/> calls the uploader makes, behind an
/// interface so tests can fake the server. <see cref="AsyncFtpClientFacade"/> is the only real
/// implementation.
/// </summary>
internal interface IFtpClientFacade : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>True once the TLS certificate callback has refused a certificate on this connection.</summary>
    bool CertificateRejected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>The remote size, or <c>-1</c> when the file doesn't exist.</summary>
    Task<long> GetFileSizeAsync(string remotePath, CancellationToken cancellationToken);

    Task<FtpStatus> UploadFileAsync(
        string localPath,
        string remotePath,
        FtpRemoteExists existsMode,
        bool createRemoteDir,
        FtpVerify verifyOptions,
        IProgress<FtpProgress> progress,
        CancellationToken cancellationToken);
}

internal interface IFtpClientFacadeFactory
{
    IFtpClientFacade Create(FtpConnectionSettings settings);
}

/// <summary>Everything one FTP connection is opened with. <see cref="ToString"/> never prints <see cref="Password"/>.</summary>
internal sealed record FtpConnectionSettings(
    string Host,
    int Port,
    string Username,
    string Password,
    FtpEncryptionMode EncryptionMode,
    bool ValidateAnyCertificate,
    int ReadTimeoutMilliseconds,
    int DataConnectionReadTimeoutMilliseconds)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Host = ").Append(Host)
            .Append(", Port = ").Append(Port)
            .Append(", Username = ").Append(Username)
            .Append(", Password = ***")
            .Append(", EncryptionMode = ").Append(EncryptionMode)
            .Append(", ValidateAnyCertificate = ").Append(ValidateAnyCertificate)
            .Append(", ReadTimeoutMilliseconds = ").Append(ReadTimeoutMilliseconds)
            .Append(", DataConnectionReadTimeoutMilliseconds = ").Append(DataConnectionReadTimeoutMilliseconds);
        return true;
    }
}
