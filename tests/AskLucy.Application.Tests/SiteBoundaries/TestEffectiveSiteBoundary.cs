using AskLucy.Application.SiteBoundaries;
using NSubstitute;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>An <see cref="EffectiveSiteBoundary"/> for tests that don't involve a hand edit: chats without a correction link never reach the repository.</summary>
internal static class TestEffectiveSiteBoundary
{
    public static EffectiveSiteBoundary None() => new(Substitute.For<ISiteBoundaryCorrectionRepository>());
}
