using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SetAiProviderCredential;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/066 US2 — the persisted hint must match the plaintext key that was actually set, and stay in sync as it's replaced (FR-005).</summary>
public sealed class SetAiProviderCredentialCommandHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AIProvider _provider;
    private readonly SetAiProviderCredentialCommandHandler _handler;

    public SetAiProviderCredentialCommandHandlerTests()
    {
        _provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        _providers.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _currentUser.UserId.Returns("admin-1");
        _credentialProtector.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");

        _handler = new SetAiProviderCredentialCommandHandler(
            _providers, _credentialProtector, _unitOfWork, _currentUser,
            Substitute.For<ILogger<SetAiProviderCredentialCommandHandler>>());
    }

    [Fact]
    public async Task Handle_ShouldPersistAHint_MatchingCredentialHintFormatter()
    {
        await _handler.Handle(new SetAiProviderCredentialCommand(_provider.Id, "sk-test-1234567890ABCDEFghij"), CancellationToken.None);

        _provider.CredentialHint.Should().Be(CredentialHintFormatter.Format("sk-test-1234567890ABCDEFghij"));
    }

    [Fact]
    public async Task Handle_ShouldOverwriteThePreviousHint_WhenCalledAgainWithADifferentKey()
    {
        await _handler.Handle(new SetAiProviderCredentialCommand(_provider.Id, "sk-test-1234567890ABCDEFghij"), CancellationToken.None);

        await _handler.Handle(new SetAiProviderCredentialCommand(_provider.Id, "zzzz-different-key-WXYZ"), CancellationToken.None);

        _provider.CredentialHint.Should().Be(CredentialHintFormatter.Format("zzzz-different-key-WXYZ"));
    }
}
