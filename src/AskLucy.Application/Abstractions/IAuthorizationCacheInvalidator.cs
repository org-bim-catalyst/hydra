namespace AskLucy.Application.Abstractions;

/// <summary>Evicts a user's cached effective-permission resolution (research.md Decision 3) so a role change is visible on their very next request.</summary>
public interface IAuthorizationCacheInvalidator
{
    void Evict(string userId);
}
