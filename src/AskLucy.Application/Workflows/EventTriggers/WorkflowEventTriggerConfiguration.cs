using System.Text.Json;

namespace AskLucy.Application.Workflows.EventTriggers;

/// <summary>
/// Parsed shape of <c>Workflow.EventTriggerConfigurationJson</c> (FR-064) — <c>{"eventType":"DocumentUploaded","knowledgeBaseId":"&lt;guid&gt;"}</c>.
/// <see cref="EventType"/> is one of <c>DocumentUploaded</c>/<c>DocumentProcessed</c>/<c>KnowledgeBaseUpdated</c>
/// (matched against <see cref="WorkflowEventTriggerHandler"/>'s own event-type tags, not a shared enum,
/// since new event types are added by extending the handler, not the Domain). <see cref="KnowledgeBaseId"/>
/// is the trigger's scope (FR-064) — null means "any knowledge base" for that owner.
/// </summary>
public sealed record WorkflowEventTriggerConfiguration(string? EventType, Guid? KnowledgeBaseId);

/// <summary>Deserializes <c>Workflow.EventTriggerConfigurationJson</c>, mirroring <c>WorkflowErrorPolicyParser</c>'s tolerant-parse shape exactly.</summary>
public static class WorkflowEventTriggerConfigurationParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Malformed, empty, or missing JSON parses to a configuration that matches nothing — never throws, and never accidentally matches every event.</summary>
    public static WorkflowEventTriggerConfiguration Parse(string? eventTriggerConfigurationJson)
    {
        if (string.IsNullOrWhiteSpace(eventTriggerConfigurationJson))
        {
            return new WorkflowEventTriggerConfiguration(null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowEventTriggerConfiguration>(eventTriggerConfigurationJson, Options)
                ?? new WorkflowEventTriggerConfiguration(null, null);
        }
        catch (JsonException)
        {
            return new WorkflowEventTriggerConfiguration(null, null);
        }
    }
}
