using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.UpdateAiProvider;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/078 research D10 — <see cref="IAiProviderSwitchedOffObserver"/> is notified only on the
/// enabled→disabled edge, before the handler's single <c>SaveChangesAsync</c>, so the provider row
/// and any dependent setting commit together.
/// </summary>
public sealed class UpdateAiProviderCommandHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAiProviderSwitchedOffObserver _observer = Substitute.For<IAiProviderSwitchedOffObserver>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public UpdateAiProviderCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-user");
    }

    private UpdateAiProviderCommandHandler CreateHandler() => new(
        _providers, _unitOfWork, _currentUser, [_observer], _timeProvider,
        NullLogger<UpdateAiProviderCommandHandler>.Instance);

    private static AIProvider EnabledProvider()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        provider.SetCredential("ciphertext", null, "system");
        provider.Enable("system");
        return provider;
    }

    [Fact]
    public async Task DisablingAnEnabledProvider_ShouldNotifyTheObserver_BeforeSaveChanges()
    {
        var provider = EnabledProvider();
        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        var callOrder = new List<string>();
        _observer.OnSwitchedOffAsync(provider.ProviderKey, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("observer"));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1))
            .AndDoes(_ => callOrder.Add("save"));

        var command = new UpdateAiProviderCommand(provider.Id, IsEnabled: false, DefaultModelId: null);
        await CreateHandler().Handle(command, CancellationToken.None);

        await _observer.Received(1).OnSwitchedOffAsync(provider.ProviderKey, Arg.Any<DateTime>(), CancellationToken.None);
        callOrder.Should().Equal("observer", "save");
    }

    [Fact]
    public async Task EnablingAProvider_ShouldNotNotifyTheObserver()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        provider.SetCredential("ciphertext", null, "system");
        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);

        var command = new UpdateAiProviderCommand(provider.Id, IsEnabled: true, DefaultModelId: null);
        await CreateHandler().Handle(command, CancellationToken.None);

        await _observer.DidNotReceiveWithAnyArgs().OnSwitchedOffAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisablingAnAlreadyDisabledProvider_ShouldNotNotifyTheObserver()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);

        var command = new UpdateAiProviderCommand(provider.Id, IsEnabled: false, DefaultModelId: null);
        await CreateHandler().Handle(command, CancellationToken.None);

        await _observer.DidNotReceiveWithAnyArgs().OnSwitchedOffAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdatingOnlyTheDefaultModel_ShouldNotNotifyTheObserver()
    {
        var provider = EnabledProvider();
        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);

        var command = new UpdateAiProviderCommand(provider.Id, IsEnabled: null, DefaultModelId: Guid.NewGuid());
        await CreateHandler().Handle(command, CancellationToken.None);

        await _observer.DidNotReceiveWithAnyArgs().OnSwitchedOffAsync(default!, default, TestContext.Current.CancellationToken);
        provider.IsEnabled.Should().BeTrue();
    }
}
