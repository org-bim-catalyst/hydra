namespace AskLucy.Infrastructure.CustomModels.HuggingFace;

/// <summary>
/// specs/072 research D4 layer 2. Every URL the Hugging Face source fetches — the first request, each
/// redirect hop and each listing cursor — must pass <see cref="IsAllowed"/>: <c>https</c> on the
/// default port, no user info, and a host that is Hugging Face's own or its CDN.
/// </summary>
internal static class HuggingFaceRedirectPolicy
{
    public const int MaxRedirects = 5;

    public static bool IsAllowed(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.HostNameType == UriHostNameType.Dns
        && (uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase));
}
