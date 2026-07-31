using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;

/// <summary>
/// research.md Decision 1 — compares the vendor's list against the provider's *entire*
/// catalog regardless of status: a model already known to the catalog in any status is
/// never proposed as an addition again (FR-006, spec.md's first clarification), and only
/// a currently-Available model missing from the vendor's list is proposed as
/// no-longer-listed (a model already Deprecated/Unavailable is never surfaced on that
/// side either — redundant noise).
/// </summary>
public sealed class GetProviderModelSyncDiffQueryHandler(
    IAIProviderRepository providers,
    IAIModelRepository models,
    IAIProviderResolver resolver) : IRequestHandler<GetProviderModelSyncDiffQuery, ProviderModelSyncDiffDto>
{
    public async Task<ProviderModelSyncDiffDto> Handle(GetProviderModelSyncDiffQuery request, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        var catalogModels = await models.ListByProviderIdAsync(request.ProviderId, cancellationToken);
        var aiProvider = resolver.Resolve(provider.ProviderKey);
        var vendorModels = await aiProvider.ListAvailableModelsAsync(cancellationToken);

        var catalogKeys = catalogModels.Select(m => m.ModelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vendorKeys = vendorModels.Select(m => m.ModelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = vendorModels.Where(v => !catalogKeys.Contains(v.ModelKey)).ToList();

        var removedFromVendor = catalogModels
            .Where(m => m.Status == AIModelStatus.Available && !vendorKeys.Contains(m.ModelKey))
            .Select(m => new RemovedModelDto(m.Id, m.ModelKey, m.DisplayName))
            .ToList();

        return new ProviderModelSyncDiffDto(added, removedFromVendor);
    }
}
