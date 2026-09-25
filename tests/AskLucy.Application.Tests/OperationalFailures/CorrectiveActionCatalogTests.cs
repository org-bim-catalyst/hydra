using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 T039 — the corrective action for every engine × kind (FR-014).</summary>
public sealed class CorrectiveActionCatalogTests
{
    private static readonly Guid ProviderId = Guid.Parse("7a1c0b7e-0000-4000-8000-000000000001");

    [Fact]
    public void For_ShouldReturnText_ForEveryEngineAndKind()
    {
        foreach (var engine in Enum.GetValues<OperationalFailureEngine>())
        {
            foreach (var kind in Enum.GetValues<OperationalFailureKind>())
            {
                CorrectiveActionCatalog.For(engine, kind, ProviderId, subject: null).Text
                    .Should().NotBeNullOrWhiteSpace($"{engine} × {kind} needs a suggested action");
            }
        }
    }

    [Theory]
    [InlineData(OperationalFailureEngine.Chat, OperationalFailureKind.CredentialRejected)]
    [InlineData(OperationalFailureEngine.AiProvider, OperationalFailureKind.CredentialUnreadable)]
    [InlineData(OperationalFailureEngine.Embeddings, OperationalFailureKind.CredentialRejected)]
    [InlineData(OperationalFailureEngine.ImageGeneration, OperationalFailureKind.CredentialRejected)]
    public void For_ShouldLinkToTheProvider_ForACredentialKind(OperationalFailureEngine engine, OperationalFailureKind kind)
    {
        var action = CorrectiveActionCatalog.For(engine, kind, ProviderId, subject: null);

        action.Text.Should().Be(CorrectiveActionCatalog.ReplaceKey);
        action.AdminRoute.Should().Be($"/admin/ai-providers?select={ProviderId}");
    }

    [Fact]
    public void For_ShouldLinkToVoice_ForAVoiceCredentialKind()
    {
        CorrectiveActionCatalog.For(OperationalFailureEngine.Voice, OperationalFailureKind.CredentialRejected, ProviderId, subject: null)
            .AdminRoute.Should().Be($"/admin/voice?select={ProviderId}");
    }

    [Fact]
    public void For_ShouldLinkToTheListWithoutSelection_WhenTheProviderIsUnknown()
    {
        CorrectiveActionCatalog.For(OperationalFailureEngine.Chat, OperationalFailureKind.CredentialRejected, providerId: null, subject: null)
            .AdminRoute.Should().Be("/admin/ai-providers");
    }

    [Theory]
    [InlineData(OperationalFailureKind.RateLimited)]
    [InlineData(OperationalFailureKind.Unavailable)]
    [InlineData(OperationalFailureKind.TimedOut)]
    [InlineData(OperationalFailureKind.DependencyUnreachable)]
    public void For_ShouldSuggestNoAction_ForATransientKind(OperationalFailureKind kind)
    {
        var action = CorrectiveActionCatalog.For(OperationalFailureEngine.Chat, kind, ProviderId, subject: null);

        action.Text.Should().Be(CorrectiveActionCatalog.NoActionUnlessPersists);
        action.AdminRoute.Should().BeNull();
        action.AdminAction.Should().BeNull();
    }

    [Theory]
    [InlineData(OperationalFailureKind.NotConfigured, "/admin/ai-capabilities")]
    [InlineData(OperationalFailureKind.QuotaExhausted, "/admin/default-models")]
    [InlineData(OperationalFailureKind.UsageRestricted, "/admin/default-models")]
    [InlineData(OperationalFailureKind.RequestInvalid, "/admin/default-models")]
    [InlineData(OperationalFailureKind.ResponseNotUnderstood, "/admin/default-models")]
    public void For_ShouldLinkToTheFixingPage(OperationalFailureKind kind, string route)
    {
        CorrectiveActionCatalog.For(OperationalFailureEngine.Chat, kind, ProviderId, subject: null).AdminRoute.Should().Be(route);
    }

    [Fact]
    public void For_ShouldOpenTheJobsDashboard_ForABackgroundJob()
    {
        var action = CorrectiveActionCatalog.For(OperationalFailureEngine.BackgroundJob, OperationalFailureKind.JobFailedAfterRetries, providerId: null, subject: null);

        action.AdminAction.Should().Be(CorrectiveAdminAction.OpenJobsDashboard);
        action.AdminRoute.Should().BeNull();
    }

    [Fact]
    public void For_ShouldSelectTheServer_ForAnMcpFailure()
    {
        var serverId = Guid.NewGuid();

        CorrectiveActionCatalog.For(OperationalFailureEngine.Mcp, OperationalFailureKind.Unavailable, providerId: null, new OperationalFailureSubject("McpServer", serverId))
            .AdminRoute.Should().Be($"/admin/mcp-servers?select={serverId}");
    }

    [Fact]
    public void For_ShouldSearchTheAccount_ForARefusedSignIn()
    {
        var action = CorrectiveActionCatalog.For(OperationalFailureEngine.Access, OperationalFailureKind.SignInRefused, providerId: null, subject: null, "ana+x@example.com");

        action.Text.Should().Be(CorrectiveActionCatalog.ReviewAccount);
        action.AdminRoute.Should().Be("/admin/users?search=ana%2Bx%40example.com");
    }

    [Fact]
    public void For_ShouldSuggestNoAction_ForAccessDenied()
    {
        CorrectiveActionCatalog.For(OperationalFailureEngine.Access, OperationalFailureKind.AccessDenied, providerId: null, subject: null)
            .Should().Be(new CorrectiveAction(CorrectiveActionCatalog.NoActionUnlessPersists));
    }

    [Theory]
    [InlineData(OperationalFailureEngine.Workflow)]
    [InlineData(OperationalFailureEngine.Agent)]
    [InlineData(OperationalFailureEngine.DocumentProcessing)]
    public void For_ShouldPointAtTheItem_ForAStepFailure(OperationalFailureEngine engine)
    {
        CorrectiveActionCatalog.For(engine, OperationalFailureKind.UnexpectedError, providerId: null, subject: null)
            .Text.Should().Be(CorrectiveActionCatalog.OpenItem);
    }
}
