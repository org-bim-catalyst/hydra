using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpPrompts;
using AskLucy.Application.Mcp.Queries.ListAvailableMcpResources;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ListAvailableMcpResourcesAndPromptsQueryHandlerTests
{
    [Fact]
    public async Task ListAvailableMcpResourcesQueryHandler_ShouldReturnEveryAvailableResource_WithItsSourceServerName()
    {
        var repository = Substitute.For<IMcpResourceRepository>();
        var resource = McpResource.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "file:///report.txt", "Report", null, "text/plain");
        repository.ListAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpResource Resource, string ServerName)>)[(resource, "Acme Docs")]);
        var handler = new ListAvailableMcpResourcesQueryHandler(repository);

        var result = await handler.Handle(new ListAvailableMcpResourcesQuery(), CancellationToken.None);

        result.Should().ContainSingle(r => r.NamespacedName == resource.NamespacedName && r.SourceServerName == "Acme Docs");
    }

    [Fact]
    public async Task ListAvailableMcpPromptsQueryHandler_ShouldReturnEveryAvailablePrompt_WithItsSourceServerName()
    {
        var repository = Substitute.For<IMcpPromptRepository>();
        var prompt = McpPrompt.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "summarize", "Summarizes text.", "Summarize the following.");
        repository.ListAvailableAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<(McpPrompt Prompt, string ServerName)>)[(prompt, "Acme Docs")]);
        var handler = new ListAvailableMcpPromptsQueryHandler(repository);

        var result = await handler.Handle(new ListAvailableMcpPromptsQuery(), CancellationToken.None);

        result.Should().ContainSingle(p => p.NamespacedName == prompt.NamespacedName && p.SourceServerName == "Acme Docs");
    }
}
