using AskLucy.Domain.Ai;
using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

/// <summary>
/// Compares a <see cref="Prompt"/>'s <see cref="PromptCapabilityRequirements"/> against a model's
/// existing <see cref="AIModelCapabilities"/> (spec.md FR-004) — reused as the in-memory comparison
/// shape, not persisted directly (data-model.md). Used by both <c>ExecutePromptCommandHandler</c>
/// (US2) and <c>InsertPromptIntoConversationCommandHandler</c> (US5).
/// </summary>
public static class PromptCapabilityChecker
{
    public static IReadOnlyList<string> GetUnmetRequirements(PromptCapabilityRequirements required, AIModelCapabilities modelCapabilities)
    {
        var missing = new List<string>();

        if (required.RequiresStreaming && !modelCapabilities.Streaming)
        {
            missing.Add("streaming");
        }

        if (required.RequiresVision && !modelCapabilities.Vision)
        {
            missing.Add("vision");
        }

        if (required.RequiresFunctionCalling && !modelCapabilities.FunctionCalling)
        {
            missing.Add("function calling");
        }

        if (required.RequiresJsonMode && !modelCapabilities.JsonMode)
        {
            missing.Add("JSON mode");
        }

        if (required.RequiresReasoning && !modelCapabilities.Reasoning)
        {
            missing.Add("reasoning");
        }

        if (required.RequiresEmbeddings && !modelCapabilities.Embeddings)
        {
            missing.Add("embeddings");
        }

        if (required.RequiresImageInput && !modelCapabilities.ImageInput)
        {
            missing.Add("image input");
        }

        if (required.RequiresImageOutput && !modelCapabilities.ImageOutput)
        {
            missing.Add("image output");
        }

        if (required.RequiresAudio && !modelCapabilities.Audio)
        {
            missing.Add("audio");
        }

        return missing;
    }

    public static bool IsCompatible(PromptCapabilityRequirements required, AIModelCapabilities modelCapabilities) =>
        GetUnmetRequirements(required, modelCapabilities).Count == 0;
}
