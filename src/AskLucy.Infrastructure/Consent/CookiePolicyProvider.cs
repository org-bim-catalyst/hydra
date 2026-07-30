using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Consent;

public sealed class CookiePolicyProvider(IOptions<CookiePolicyOptions> options) : ICookiePolicyProvider
{
    public (string Version, DateTime EffectiveAtUtc) GetCurrentPolicy()
    {
        var value = options.Value;
        return (value.CurrentVersion, value.EffectiveAtUtc);
    }
}
