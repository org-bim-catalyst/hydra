using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 FR-018 / FR-026a — what makes two failures the same incident, and the same root cause.</summary>
public sealed class OperationalFailureKeysTests
{
    private static readonly Guid WorkflowId = Guid.Parse("7d2f7a57-1f7e-4f55-9d8a-1f6d8a2b3c4d");

    private static OperationalFailureReport Report(
        OperationalFailureEngine engine = OperationalFailureEngine.Voice,
        string? provider = "ElevenLabs",
        string? model = "eleven_turbo_v2",
        OperationalFailureKind kind = OperationalFailureKind.CredentialRejected,
        string operation = "Text-to-speech",
        OperationalFailureSubject? subject = null,
        string? userId = "user-1",
        Guid? chatId = null) => new()
        {
            Engine = engine,
            ProviderName = provider,
            Model = model,
            Kind = kind,
            Operation = operation,
            Subject = subject,
            Reason = "Text-to-speech request failed",
            References = new OperationalFailureReferences { UserId = userId, ChatId = chatId },
        };

    [Fact]
    public void Grouping_IsLowercaseSha256Hex()
    {
        OperationalFailureKeys.Grouping(Report()).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Grouping_IsStableUnderCaseAndWhitespace()
    {
        var a = OperationalFailureKeys.Grouping(Report(provider: "ElevenLabs", model: "eleven_turbo_v2", operation: "Text-to-speech"));
        var b = OperationalFailureKeys.Grouping(Report(provider: "  elevenlabs ", model: "ELEVEN_TURBO_V2", operation: "text-to-SPEECH  "));

        b.Should().Be(a);
    }

    [Fact]
    public void Grouping_CollapsesInternalWhitespace()
    {
        OperationalFailureKeys.Grouping(Report(operation: "Chat   reply"))
            .Should().Be(OperationalFailureKeys.Grouping(Report(operation: "Chat reply")));
    }

    public static TheoryData<OperationalFailureReport> Variants => new()
    {
        Report(engine: OperationalFailureEngine.Chat),
        Report(provider: "OpenAI"),
        Report(model: "eleven_multilingual_v2"),
        Report(kind: OperationalFailureKind.QuotaExhausted),
        Report(operation: "Transcription"),
        Report(subject: new OperationalFailureSubject("Workflow", WorkflowId)),
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void Grouping_DiffersWhenAnyGroupingPartDiffers(OperationalFailureReport variant)
    {
        OperationalFailureKeys.Grouping(variant).Should().NotBe(OperationalFailureKeys.Grouping(Report()));
    }

    [Fact]
    public void Grouping_DiffersBetweenSubjects()
    {
        var first = Report(subject: new OperationalFailureSubject("Workflow", WorkflowId));
        var second = Report(subject: new OperationalFailureSubject("Workflow", Guid.NewGuid()));

        OperationalFailureKeys.Grouping(first).Should().NotBe(OperationalFailureKeys.Grouping(second));
    }

    [Fact]
    public void Grouping_IgnoresTheSubjectLabel()
    {
        var first = Report(subject: new OperationalFailureSubject("Workflow", WorkflowId, "Nightly import"));
        var renamed = Report(subject: new OperationalFailureSubject("Workflow", WorkflowId, "Nightly import (v2)"));

        OperationalFailureKeys.Grouping(renamed).Should().Be(OperationalFailureKeys.Grouping(first));
    }

    [Fact]
    public void Grouping_IsIdenticalAcrossChatsUsersAndRuns()
    {
        var first = Report(userId: "user-1", chatId: Guid.NewGuid());
        var second = Report(userId: "user-2", chatId: Guid.NewGuid()) with
        {
            References = new OperationalFailureReferences { UserId = "user-2", WorkflowExecutionId = Guid.NewGuid(), JobId = "42" },
        };

        OperationalFailureKeys.Grouping(second).Should().Be(OperationalFailureKeys.Grouping(first));
    }

    [Fact]
    public void RootCause_ForAProviderKind_IgnoresEngineOperationAndSubject()
    {
        var voice = Report(engine: OperationalFailureEngine.Voice, operation: "Text-to-speech");
        var chat = Report(engine: OperationalFailureEngine.Chat, operation: "Chat reply", subject: new OperationalFailureSubject("Agent", Guid.NewGuid()));

        OperationalFailureKeys.RootCause(chat).Should().Be(OperationalFailureKeys.RootCause(voice));
        OperationalFailureKeys.RootCause(Report(model: "other")).Should().NotBe(OperationalFailureKeys.RootCause(voice));
    }

    [Fact]
    public void RootCause_ForANonProviderKind_IsEngineKindAndOperation()
    {
        var first = Report(engine: OperationalFailureEngine.Workflow, provider: null, model: null, kind: OperationalFailureKind.UnexpectedError, operation: "Http request",
            subject: new OperationalFailureSubject("Workflow", WorkflowId));
        var otherSubject = first with { Subject = new OperationalFailureSubject("Workflow", Guid.NewGuid()), ProviderName = "Something" };

        OperationalFailureKeys.RootCause(otherSubject).Should().Be(OperationalFailureKeys.RootCause(first));
        OperationalFailureKeys.RootCause(first with { Operation = "Condition" }).Should().NotBe(OperationalFailureKeys.RootCause(first));
        OperationalFailureKeys.RootCause(first with { Engine = OperationalFailureEngine.Agent }).Should().NotBe(OperationalFailureKeys.RootCause(first));
    }

    [Fact]
    public void Grouping_ForAVoiceRecovery_MatchesTheFailoverReportBuiltFromTheSameInputs()
    {
        var failover = Report() with { IsFailover = true, Outcome = OperationalFailureOutcome.DegradedServed };
        var recovery = new VoiceRecoveryReport
        {
            ProviderName = "ElevenLabs",
            Model = "eleven_turbo_v2",
            Kind = OperationalFailureKind.CredentialRejected,
            Operation = "Text-to-speech",
            UserId = "user-9",
        };

        OperationalFailureKeys.Grouping(recovery).Should().Be(OperationalFailureKeys.Grouping(failover));
    }
}
