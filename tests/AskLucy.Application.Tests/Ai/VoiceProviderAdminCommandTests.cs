using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.AddVoiceProvider;
using AskLucy.Application.Ai.Commands.PreviewVoice;
using AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;
using AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/070 — the admin voice page's commands: add a provider, set its key, make a voice Lucy's, preview a voice.</summary>
public sealed class VoiceProviderAdminCommandTests
{
    private readonly IVoiceProviderRepository _repository = Substitute.For<IVoiceProviderRepository>();
    private readonly IAiCredentialProtector _protector = Substitute.For<IAiCredentialProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ITextToSpeechEngine _elevenLabs = Engine("ElevenLabs", "ElevenLabs", requiresCredential: true);
    private readonly ITextToSpeechEngine _supertonic = Engine("Supertonic", "Supertonic (on-server)", requiresCredential: false);
    private readonly List<VoiceProvider> _rows = [];

    public VoiceProviderAdminCommandTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _protector.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");
        _protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>().Replace("protected:", string.Empty, StringComparison.Ordinal));
        _repository.ListByPriorityAsync(Arg.Any<CancellationToken>()).Returns(_ => _rows.OrderBy(r => r.Priority).ToList());
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _rows.FirstOrDefault(r => r.Id == call.Arg<Guid>()));
    }

    private ITextToSpeechEngine[] Engines => [_elevenLabs, _supertonic];

    private static ITextToSpeechEngine Engine(string key, string name, bool requiresCredential)
    {
        var engine = Substitute.For<ITextToSpeechEngine>();
        engine.ProviderKey.Returns(key);
        engine.DisplayName.Returns(name);
        engine.RequiresCredential.Returns(requiresCredential);
        engine.ResolveDefaultSettings(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => new VoiceSettingsDto(call.ArgAt<string?>(1) ?? "default", "model", 0, 0, 0, 1.0, false, "mp3", call.ArgAt<string>(0)));
        return engine;
    }

    private VoiceProvider AddRow(string key, int priority, string? ciphertext = null)
    {
        var row = VoiceProvider.Create(key, key, priority, "system");
        if (ciphertext is not null)
        {
            row.SetCredential(ciphertext, null, "system");
        }

        _rows.Add(row);
        return row;
    }

    private static async IAsyncEnumerable<byte[]> Chunks(params byte[][] chunks)
    {
        await Task.Yield();
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    private AddVoiceProviderCommandHandler AddHandler() => new(
        _repository, Engines, _protector, _unitOfWork, _currentUser, Substitute.For<ILogger<AddVoiceProviderCommandHandler>>());

    private SetPrimaryVoiceProviderCommandHandler PrimaryHandler() => new(
        _repository, Engines, _unitOfWork, _currentUser, Substitute.For<ILogger<SetPrimaryVoiceProviderCommandHandler>>());

    private SetVoiceProviderCredentialCommandHandler CredentialHandler() => new(
        _repository, Engines, _protector, _unitOfWork, _currentUser, Substitute.For<ILogger<SetVoiceProviderCredentialCommandHandler>>());

    private PreviewVoiceCommandHandler PreviewHandler() => new(
        _repository, Engines, _protector, _currentUser, Substitute.For<ILogger<PreviewVoiceCommandHandler>>());

    [Fact]
    public async Task AddVoiceProvider_ShouldJoinTheEndOfTheFailoverOrder()
    {
        AddRow("ElevenLabs", 0);

        var added = await AddHandler().Handle(new AddVoiceProviderCommand("supertonic", null), CancellationToken.None);

        added.ProviderKey.Should().Be("Supertonic", "the engine's own key is stored, whatever casing the request used");
        added.DisplayName.Should().Be("Supertonic (on-server)");
        added.Priority.Should().Be(1);
        added.IsPrimary.Should().BeFalse();
        added.HasCredential.Should().BeFalse();
        _repository.Received(1).Add(Arg.Is<VoiceProvider>(p => p.ProviderKey == "Supertonic"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddVoiceProvider_ShouldEncryptTheKey_AndReturnOnlyItsHint()
    {
        var added = await AddHandler().Handle(new AddVoiceProviderCommand("ElevenLabs", " sk_test_1234567890abcdWXYZ "), CancellationToken.None);

        added.IsPrimary.Should().BeTrue("the first provider added is Lucy's voice");
        added.HasCredential.Should().BeTrue();
        added.CredentialHint.Should().Be(CredentialHintFormatter.Format("sk_test_1234567890abcdWXYZ"));
        _repository.Received(1).Add(Arg.Is<VoiceProvider>(p => p.CredentialCiphertext == "protected:sk_test_1234567890abcdWXYZ"));
    }

    [Fact]
    public async Task AddVoiceProvider_ShouldReject_AnAlreadyAddedEngine()
    {
        AddRow("ElevenLabs", 0);

        var act = () => AddHandler().Handle(new AddVoiceProviderCommand("elevenlabs", null), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateResourceException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddVoiceProvider_ShouldReject_AnEngineThisServerDoesNotHave()
    {
        var act = () => AddHandler().Handle(new AddVoiceProviderCommand("Piper", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SetPrimaryVoiceProvider_ShouldPutTheChosenProviderFirst_AndRenumberTheRestInOrder()
    {
        var elevenLabs = AddRow("ElevenLabs", 0);
        var supertonic = AddRow("Supertonic", 3);

        var result = await PrimaryHandler().Handle(new SetPrimaryVoiceProviderCommand(supertonic.Id, "F2"), CancellationToken.None);

        supertonic.Priority.Should().Be(0);
        supertonic.DefaultVoiceId.Should().Be("F2");
        elevenLabs.Priority.Should().Be(1);
        result.Select(p => (p.ProviderKey, p.Priority, p.IsPrimary)).Should().Equal(
            ("Supertonic", 0, true), ("ElevenLabs", 1, false));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimaryVoiceProvider_ShouldReject_AnUnknownProvider()
    {
        AddRow("ElevenLabs", 0);

        var act = () => PrimaryHandler().Handle(new SetPrimaryVoiceProviderCommand(Guid.NewGuid(), "F2"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SetVoiceProviderCredential_ShouldReject_AnEngineThatRunsOnTheServer()
    {
        var supertonic = AddRow("Supertonic", 0);

        var act = () => CredentialHandler().Handle(new SetVoiceProviderCredentialCommand(supertonic.Id, "sk_anything"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        supertonic.CredentialCiphertext.Should().BeNull();
    }

    [Fact]
    public async Task SetVoiceProviderCredential_ShouldStoreTheEncryptedKey()
    {
        var elevenLabs = AddRow("ElevenLabs", 0);

        var result = await CredentialHandler().Handle(new SetVoiceProviderCredentialCommand(elevenLabs.Id, "sk_test_1234567890abcdWXYZ"), CancellationToken.None);

        elevenLabs.CredentialCiphertext.Should().Be("protected:sk_test_1234567890abcdWXYZ");
        result.HasCredential.Should().BeTrue();
        result.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewVoice_ShouldSpeakWithTheRequestedVoiceAndKey_AndReturnMp3()
    {
        var elevenLabs = AddRow("ElevenLabs", 0, "protected:key-1");
        _elevenLabs.StreamSpeechAsync(Arg.Any<string>(), Arg.Any<VoiceSettingsDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Chunks([1, 2], [3]));

        var preview = await PreviewHandler().Handle(new PreviewVoiceCommand(elevenLabs.Id, "adam", " Testing. ", "ar"), CancellationToken.None);

        preview.ContentType.Should().Be("audio/mpeg");
        Convert.FromBase64String(preview.AudioBase64).Should().Equal(1, 2, 3);
        _elevenLabs.Received(1).StreamSpeechAsync(
            "Testing.",
            Arg.Is<VoiceSettingsDto>(s => s.VoiceId == "adam" && s.Language == "ar" && s.ProviderKey == "ElevenLabs"),
            "key-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewVoice_ShouldFail_WhenTheEngineReturnsNoAudio()
    {
        var supertonic = AddRow("Supertonic", 0);
        _supertonic.StreamSpeechAsync(Arg.Any<string>(), Arg.Any<VoiceSettingsDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Chunks());

        var act = () => PreviewHandler().Handle(new PreviewVoiceCommand(supertonic.Id, "F1", "Hello.", "en"), CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public async Task PreviewVoice_ShouldReportAnUnreadableKey_RatherThanTheCryptographicDetail()
    {
        var elevenLabs = AddRow("ElevenLabs", 0, "corrupt");
        _protector.Unprotect("corrupt").Returns(_ => throw new CryptographicException("key ring changed"));

        var act = () => PreviewHandler().Handle(new PreviewVoiceCommand(elevenLabs.Id, "adam", "Hello.", "en"), CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderCredentialUnreadableException>();
    }

    [Theory]
    [InlineData("adam", "Hello.", "en", true)]
    [InlineData("adam", "Hello.", "pt-BR", true)]
    [InlineData("", "Hello.", "en", false)]
    [InlineData("adam", "", "en", false)]
    [InlineData("adam", "... !!", "en", false)]
    [InlineData("adam", "Hello.", "english", false)]
    [InlineData("adam", "Hello.", "", false)]
    public void PreviewVoiceValidator_ShouldAcceptOnlySpeakableRequests(string voiceId, string text, string language, bool valid)
    {
        var result = new PreviewVoiceCommandValidator().Validate(new PreviewVoiceCommand(Guid.NewGuid(), voiceId, text, language));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void PreviewVoiceValidator_ShouldReject_AnOverlongSampleSentence()
    {
        var text = new string('a', PreviewVoiceCommandValidator.MaxTextLength + 1);

        var result = new PreviewVoiceCommandValidator().Validate(new PreviewVoiceCommand(Guid.NewGuid(), "adam", text, "en"));

        result.IsValid.Should().BeFalse();
    }
}
