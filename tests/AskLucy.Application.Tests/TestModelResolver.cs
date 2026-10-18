using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using NSubstitute;

namespace AskLucy.Application.Tests;

/// <summary>specs/069: an Available model resolves to itself, which is all these tests need.</summary>
internal static class TestModelResolver
{
    public static IExecutableModelResolver Passthrough() =>
        new ExecutableModelResolver(Substitute.For<IAIModelRepository>(), Substitute.For<IModelDeprecationRepository>());
}
