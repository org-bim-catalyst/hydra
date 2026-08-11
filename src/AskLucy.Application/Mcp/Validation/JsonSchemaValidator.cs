using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using Json.Schema;

namespace AskLucy.Application.Mcp.Validation;

/// <summary>
/// Wraps <c>JsonSchema.Net</c> (research.md Decision 9) — the only place in this codebase that
/// validates a JSON Schema document supplied by an untrusted external party at runtime, unlike
/// every native <c>IAgentTool</c>, which hand-validates its own fixed, known shape.
/// </summary>
public sealed class JsonSchemaValidator : IJsonSchemaValidator
{
    public IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes)
    {
        var instanceByteCount = Encoding.UTF8.GetByteCount(instance.GetRawText());
        if (instanceByteCount > maxSizeBytes)
        {
            return [$"Payload size {instanceByteCount} bytes exceeds the maximum of {maxSizeBytes} bytes."];
        }

        JsonSchema compiledSchema;
        try
        {
            compiledSchema = JsonSchema.Build(schema);
        }
        catch (Exception ex)
        {
            // The schema document itself is arbitrary, externally-supplied data from an untrusted
            // MCP server (research.md Decision 9) — any parse failure is converted into an ordinary
            // validation-failure result here, never left to propagate as an unhandled exception.
            return [$"The tool's declared schema is malformed and could not be parsed: {ex.Message}"];
        }

        var results = compiledSchema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return [];
        }

        var messages = new List<string>();
        CollectErrors(results, messages);
        return messages.Count > 0 ? messages : ["The payload does not conform to the declared schema."];
    }

    private static void CollectErrors(EvaluationResults results, List<string> messages)
    {
        if (results.Errors is { Count: > 0 })
        {
            foreach (var (keyword, message) in results.Errors)
            {
                messages.Add($"{results.InstanceLocation} ({keyword}): {message}");
            }
        }

        if (results.Details is not null)
        {
            foreach (var detail in results.Details)
            {
                CollectErrors(detail, messages);
            }
        }
    }
}
