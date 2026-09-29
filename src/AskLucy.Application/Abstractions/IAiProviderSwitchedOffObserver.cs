namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/078 research D10 — notified on the enabled→disabled edge of an AI provider, before
/// <c>UpdateAiProviderCommandHandler</c>'s own <c>SaveChangesAsync</c>, so the provider row and any
/// dependent setting commit in the same unit of work and can never disagree.
/// </summary>
public interface IAiProviderSwitchedOffObserver
{
    Task OnSwitchedOffAsync(string providerKey, DateTime utcNow, CancellationToken cancellationToken);
}
