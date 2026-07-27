using AskLucy.Domain.Authentication;

namespace AskLucy.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(Guid tokenFamilyId, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);
}
