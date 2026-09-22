using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Web.StartupTasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>specs/066 FR-009 — the one-time startup backfill of <see cref="AIProvider.CredentialHint"/> for credentials configured before this feature shipped.</summary>
public sealed class CredentialHintBackfillServiceTests
{
    private static ServiceProvider BuildServices(
        IAIProviderRepository providers, IAiCredentialProtector protector, IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(providers);
        services.AddSingleton(protector);
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_ShouldBackfillTheHint_ForARowMissingOne()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", hint: null, "admin-1");

        var providers = Substitute.For<IAIProviderRepository>();
        providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        var protector = Substitute.For<IAiCredentialProtector>();
        protector.Unprotect("ciphertext").Returns("sk-test-1234567890ABCDEFghij");
        var unitOfWork = Substitute.For<IUnitOfWork>();

        await CredentialHintBackfillService.RunAsync(
            BuildServices(providers, protector, unitOfWork), Substitute.For<ILogger>());

        provider.CredentialHint.Should().Be("sk-t...ghij");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldSkipRows_ThatAlreadyHaveAHint()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", "sk-t...ghij", "admin-1");

        var providers = Substitute.For<IAIProviderRepository>();
        providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        var protector = Substitute.For<IAiCredentialProtector>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        await CredentialHintBackfillService.RunAsync(
            BuildServices(providers, protector, unitOfWork), Substitute.For<ILogger>());

        protector.DidNotReceive().Unprotect(Arg.Any<string>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldSkipARowThatFailsToDecrypt_WithoutBlockingTheOthers()
    {
        var badProvider = AIProvider.Create("anthropic", "Anthropic", "admin-1");
        badProvider.SetCredential("bad-ciphertext", hint: null, "admin-1");
        var goodProvider = AIProvider.Create("openai", "OpenAI", "admin-1");
        goodProvider.SetCredential("good-ciphertext", hint: null, "admin-1");

        var providers = Substitute.For<IAIProviderRepository>();
        providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([badProvider, goodProvider]);
        var protector = Substitute.For<IAiCredentialProtector>();
        protector.Unprotect("bad-ciphertext")
            .Returns(_ => throw new System.Security.Cryptography.CryptographicException("bad key ring"));
        protector.Unprotect("good-ciphertext").Returns("sk-test-1234567890ABCDEFghij");
        var unitOfWork = Substitute.For<IUnitOfWork>();

        await CredentialHintBackfillService.RunAsync(
            BuildServices(providers, protector, unitOfWork), Substitute.For<ILogger>());

        badProvider.CredentialHint.Should().BeNull();
        goodProvider.CredentialHint.Should().Be("sk-t...ghij");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
