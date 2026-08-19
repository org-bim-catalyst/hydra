using System.Text.Json;
using AskLucy.Application.Abstractions;
using ModelContextProtocol.Protocol;
using SdkMcpClient = ModelContextProtocol.Client.McpClient;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>Wraps one connected <see cref="SdkMcpClient"/> — the only place in this codebase the <c>ModelContextProtocol</c> SDK is referenced directly (research.md Decision 2, constitution §3).</summary>
public sealed class McpClient(SdkMcpClient sdkClient) : IMcpClient
{
    public async Task<IReadOnlyList<McpDiscoveredTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = await sdkClient.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(t => new McpDiscoveredTool(t.Name, t.Title, t.Description, t.ProtocolTool.InputSchema, t.ProtocolTool.OutputSchema)).ToList();
    }

    public async Task<IReadOnlyList<McpDiscoveredResource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = await sdkClient.ListResourcesAsync(cancellationToken: cancellationToken);
        return resources.Select(r => new McpDiscoveredResource(r.Uri, r.Name, r.Description, r.MimeType)).ToList();
    }

    public async Task<IReadOnlyList<McpDiscoveredPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        var prompts = await sdkClient.ListPromptsAsync(cancellationToken: cancellationToken);
        return prompts.Select(p => new McpDiscoveredPrompt(p.Name, p.Description)).ToList();
    }

    public async Task<McpToolCallResult> CallToolAsync(string toolName, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var arguments = input.RootElement.ValueKind == JsonValueKind.Object
            ? input.RootElement.EnumerateObject().ToDictionary(p => p.Name, object (p) => p.Value)
            : new Dictionary<string, object>();

        var result = await sdkClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        var isError = result.IsError ?? false;
        if (isError)
        {
            return new McpToolCallResult(true, null, ExtractText(result.Content));
        }

        var output = result.StructuredContent is { } structured
            ? JsonDocument.Parse(structured.GetRawText())
            : JsonSerializer.SerializeToDocument(result.Content, ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        return new McpToolCallResult(false, output, null);
    }

    public async Task<JsonDocument> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        var result = await sdkClient.ReadResourceAsync(uri, cancellationToken: cancellationToken);
        return JsonSerializer.SerializeToDocument(result.Contents, ModelContextProtocol.McpJsonUtilities.DefaultOptions);
    }

    public async Task<string> GetPromptAsync(string name, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken = default)
    {
        var argumentObjects = arguments?.ToDictionary(kv => kv.Key, object (kv) => kv.Value);
        var result = await sdkClient.GetPromptAsync(name, argumentObjects, cancellationToken: cancellationToken);
        return string.Join('\n', result.Messages.Select(m => ExtractText(m.Content)));
    }

    public async Task PingAsync(CancellationToken cancellationToken = default) => await sdkClient.PingAsync(cancellationToken: cancellationToken);

    public ValueTask DisposeAsync() => sdkClient.DisposeAsync();

    private static string ExtractText(ContentBlock content) => content switch
    {
        TextContentBlock text => text.Text,
        _ => JsonSerializer.Serialize(content, ModelContextProtocol.McpJsonUtilities.DefaultOptions),
    };

    private static string ExtractText(IEnumerable<ContentBlock> content) => string.Join('\n', content.Select(ExtractText));
}
