using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace AskLucy.Web.Auth;

/// <summary>Evicts the same <see cref="IMemoryCache"/> entry <see cref="CurrentAuthorizationClaimsTransformation"/> reads, so a role change is visible on the affected user's very next request rather than waiting out the 30-second safety-net expiry.</summary>
public sealed class MemoryCacheAuthorizationCacheInvalidator(IMemoryCache cache) : IAuthorizationCacheInvalidator
{
    public void Evict(string userId) => cache.Remove(CurrentAuthorizationClaimsTransformation.CacheKey(userId));
}
