using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace AskLucy.Web.Auth;

/// <summary>Evicts the same <see cref="IMemoryCache"/> entry <see cref="ActiveSessionTokenValidator"/> reads, so a revoked session is refused on its very next request rather than waiting out the 30-second safety-net expiry.</summary>
public sealed class MemoryCacheSessionRevocationCache(IMemoryCache cache) : ISessionRevocationCache
{
    public void Evict(Guid tokenFamilyId) => cache.Remove(ActiveSessionTokenValidator.CacheKey(tokenFamilyId));
}
