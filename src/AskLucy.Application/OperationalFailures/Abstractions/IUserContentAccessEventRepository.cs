using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>
/// The immutable content-access trail (FR-016c). Written synchronously before content is loaded
/// (research D15), never purged by retention; the owner's erasure only anonymises it.
/// </summary>
public interface IUserContentAccessEventRepository
{
    Task AddAsync(UserContentAccessEvent accessEvent, CancellationToken cancellationToken = default);

    Task AnonymizeOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
