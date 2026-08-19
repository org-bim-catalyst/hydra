using System.Text.Json;
using AskLucy.Application.Workflows.Expressions;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Converts between plain, user-facing JSON (a node's <c>InputsJson</c>/<c>OutputJson</c>, always
/// a flat object) and the flattened <c>path -&gt; WorkflowExpressionValue</c> dictionary the
/// expression engine evaluates against (FR-025). Each top-level property of a JSON object becomes
/// one dictionary entry under <paramref name="prefix"/> — e.g. flattening <c>{"text":"hi"}</c>
/// under prefix <c>"workflow"</c> produces the single entry <c>"workflow.text" -&gt; "hi"</c>.
/// </summary>
public static class WorkflowResolvedValues
{
    public static void AddFlattened(Dictionary<string, WorkflowExpressionValue> resolvedValues, string prefix, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            resolvedValues[$"{prefix}.{property.Name}"] = ToExpressionValue(property.Value);
        }
    }

    public static WorkflowExpressionValue ToExpressionValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => WorkflowExpressionValue.OfString(element.GetString() ?? string.Empty),
        JsonValueKind.Number => WorkflowExpressionValue.OfNumber(element.GetDouble()),
        JsonValueKind.True => WorkflowExpressionValue.OfBoolean(true),
        JsonValueKind.False => WorkflowExpressionValue.OfBoolean(false),
        JsonValueKind.Array => WorkflowExpressionValue.OfCollection(element.EnumerateArray().Select(ToExpressionValue).ToList()),
        _ => WorkflowExpressionValue.Null,
    };

    /// <summary>Serializes a snapshot dictionary to the flat JSON object <see cref="IWorkflowNodeExecutor"/> implementations receive as <c>input</c> (contracts/workflow-node-contract.md).</summary>
    public static JsonDocument ToInputDocument(IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (path, value) in resolvedValues)
            {
                writer.WritePropertyName(path);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    public static Dictionary<string, WorkflowExpressionValue> ParseInputDocument(JsonDocument document)
    {
        var result = new Dictionary<string, WorkflowExpressionValue>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = ToExpressionValue(property.Value);
        }

        return result;
    }

    private static void WriteValue(Utf8JsonWriter writer, WorkflowExpressionValue value)
    {
        switch (value.Kind)
        {
            case WorkflowExpressionValueKind.String:
                writer.WriteStringValue(value.StringValue ?? string.Empty);
                break;
            case WorkflowExpressionValueKind.Number:
                writer.WriteNumberValue(value.NumberValue ?? 0);
                break;
            case WorkflowExpressionValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue ?? false);
                break;
            case WorkflowExpressionValueKind.Collection:
                writer.WriteStartArray();
                foreach (var item in value.CollectionValue ?? [])
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
