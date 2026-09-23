using System.Net.Sockets;
using System.Security.Authentication;
using AskLucy.Application.CustomModels.Abstractions;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.CustomModels.Deployment;

/// <summary>
/// specs/072 research D2. Uploads over FTPS with FluentFTP. Explicit TLS unless the configuration
/// opts into plain FTP; never <see cref="FtpEncryptionMode.Auto"/>, which would fall back to plain FTP
/// silently. This is the only place the relative path is joined to the root path, and no message it
/// produces contains the host, root path or password.
/// </summary>
internal sealed partial class FluentFtpDeploymentFileUploader(
    IFtpClientFacadeFactory clientFactory,
    ILogger<FluentFtpDeploymentFileUploader> logger) : IDeploymentFileUploader
{
    internal const int TimeoutMilliseconds = 60_000;

    public async Task<IDeploymentUploadSession> OpenSessionAsync(DeploymentTargetSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var client = clientFactory.Create(BuildConnectionSettings(settings));
        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await client.DisposeAsync();
            throw Translate(ex, client, relativePath: null);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        return new Session(client, settings.RootPath, logger);
    }

    internal static FtpConnectionSettings BuildConnectionSettings(DeploymentTargetSettings settings) =>
        new(
            settings.Host,
            settings.Port,
            settings.Username,
            settings.Password,
            settings.AllowPlainFtp ? FtpEncryptionMode.None : FtpEncryptionMode.Explicit,
            ValidateAnyCertificate: false,
            TimeoutMilliseconds,
            TimeoutMilliseconds);

    /// <summary><c>{RootPath}/{relative}</c> with exactly one slash between them.</summary>
    internal static string CombineRemotePath(string rootPath, string relativePath) =>
        $"{rootPath.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    /// <summary>
    /// Maps a FluentFTP failure onto a safe <see cref="DeploymentTargetException"/>. The original is
    /// kept as the inner exception and never put into the message: server replies can echo paths.
    /// </summary>
    internal static DeploymentTargetException Translate(Exception exception, IFtpClientFacade client, string? relativePath)
    {
        var cause = Unwrap(exception);
        var (kind, message) = cause switch
        {
            FtpAuthenticationException or FtpCommandException { CompletionCode: "530" } => (
                DeploymentTargetFailureKind.AuthRejected,
                "The deployment server rejected the login. Check the FTP username and password in the server configuration."),
            FtpSecurityNotAvailableException => (
                DeploymentTargetFailureKind.TlsNotAccepted,
                "The deployment server doesn't accept an encrypted (FTPS) connection."),
            FtpInvalidCertificateException => (
                DeploymentTargetFailureKind.CertificateInvalid,
                "The deployment server's TLS certificate is not trusted."),
            AuthenticationException when client.CertificateRejected => (
                DeploymentTargetFailureKind.CertificateInvalid,
                "The deployment server's TLS certificate is not trusted."),
            AuthenticationException => (
                DeploymentTargetFailureKind.TlsNotAccepted,
                "The TLS handshake with the deployment server failed."),
            FtpCommandException command => (
                DeploymentTargetFailureKind.WriteRejected,
                relativePath is null
                    ? $"The deployment server refused the request (FTP {command.CompletionCode})."
                    : $"The deployment server refused to write '{relativePath}' (FTP {command.CompletionCode})."),
            _ => (
                DeploymentTargetFailureKind.ConnectionLost,
                relativePath is null
                    ? "The connection to the deployment server failed."
                    : $"The connection to the deployment server was lost while transferring '{relativePath}'."),
        };

        return new DeploymentTargetException(kind, message, exception);
    }

    /// <summary>FluentFTP wraps socket, TLS and I/O failures in a bare <see cref="FtpException"/> ("see InnerException").</summary>
    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is FtpException { InnerException: { } inner }
            and not FtpCommandException
            and not FtpSecurityNotAvailableException
            and not FtpInvalidCertificateException)
        {
            current = inner;
        }

        return current;
    }

    private static bool IsConnectionFailure(Exception exception) =>
        Unwrap(exception) is IOException or TimeoutException or SocketException or FtpMissingSocketException;

    private sealed class Session(IFtpClientFacade client, string rootPath, ILogger logger) : IDeploymentUploadSession
    {
        public async Task<long?> GetRemoteFileSizeAsync(string remoteRelativePath, CancellationToken cancellationToken = default)
        {
            // Each file starts here, so this is the "between files" point where one reconnect is allowed.
            await EnsureConnectedAsync(remoteRelativePath, cancellationToken);
            try
            {
                var size = await client.GetFileSizeAsync(CombineRemotePath(rootPath, remoteRelativePath), cancellationToken);
                return size < 0 ? null : size;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Translate(ex, client, remoteRelativePath);
            }
        }

        public async Task UploadAsync(string localPath, string remoteRelativePath, IProgress<long> progress, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(progress);

            FtpStatus status;
            try
            {
                status = await client.UploadFileAsync(
                    localPath,
                    CombineRemotePath(rootPath, remoteRelativePath),
                    FtpRemoteExists.Overwrite,
                    createRemoteDir: true,
                    FtpVerify.None,
                    new TransferredBytesProgress(progress),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !IsLocalFileFailure(ex))
            {
                throw Translate(ex, client, remoteRelativePath);
            }

            if (status != FtpStatus.Success)
            {
                throw new DeploymentTargetException(
                    DeploymentTargetFailureKind.WriteRejected,
                    $"The deployment server didn't accept '{remoteRelativePath}'.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // Every file is already uploaded or the job has already failed; a failed QUIT changes neither.
                LogDisconnectFailed(logger, ex);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        private async Task EnsureConnectedAsync(string remoteRelativePath, CancellationToken cancellationToken)
        {
            if (client.IsConnected)
            {
                return;
            }

            LogReconnecting(logger, remoteRelativePath);
            try
            {
                await client.ConnectAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var translated = Translate(ex, client, relativePath: null);
                throw IsConnectionFailure(ex)
                    ? new DeploymentTargetException(
                        DeploymentTargetFailureKind.ConnectionLost,
                        $"The connection to the deployment server was lost before '{remoteRelativePath}' and couldn't be re-established.",
                        ex)
                    : translated;
            }
        }

        /// <summary>The local temp file is the job's concern; its failure surfaces as the job's own error.</summary>
        private static bool IsLocalFileFailure(Exception exception) =>
            exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException;
    }

    /// <summary>Synchronous, unlike <see cref="Progress{T}"/>: reports arrive in order on the uploading thread.</summary>
    private sealed class TransferredBytesProgress(IProgress<long> inner) : IProgress<FtpProgress>
    {
        public void Report(FtpProgress value) => inner.Report(value.TransferredBytes);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The deployment server connection dropped between files; reconnecting before {RelativePath}")]
    private static partial void LogReconnecting(ILogger logger, string relativePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the deployment server connection failed")]
    private static partial void LogDisconnectFailed(ILogger logger, Exception exception);
}
