using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;

namespace AskLucy.Application.Ai.Dictation;

/// <summary>
/// specs/078 research D10/FR-018 — switching an AI provider off reverts any dictation choice that
/// depended on it back to Local Whisper. Mutates the tracked <see cref="DictationEngineSetting"/> in
/// place; the caller (<c>UpdateAiProviderCommandHandler</c>) commits both changes in one
/// <c>SaveChangesAsync</c>.
/// </summary>
public sealed class DictationEngineSettingSwitchOffObserver(IDictationEngineSettingRepository settings) : IAiProviderSwitchedOffObserver
{
    public async Task OnSwitchedOffAsync(string providerKey, DateTime utcNow, CancellationToken cancellationToken)
    {
        var setting = await settings.GetOrCreateAsync(cancellationToken);
        setting.RevertToLocalWhisper(providerKey, DictationRevertReason.VendorSwitchedOff, utcNow);
    }
}
