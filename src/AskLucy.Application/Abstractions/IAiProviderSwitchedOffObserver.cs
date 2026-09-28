namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/078 research D10 — told when an administrator switches a vendor off under Admin → AI
/// providers, before that change is saved, so anything depending on the vendor changes in the
/// same transaction.
/// </summary>
public interface IAiProviderSwitchedOffObserver
{
    Task OnSwitchedOffAsync(string providerKey, CancellationToken cancellationToken = default);
}
