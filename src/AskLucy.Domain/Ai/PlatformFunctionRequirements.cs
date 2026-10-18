namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/069 research D2: the capability a platform function needs from the model that serves it.
/// A replacement that lacks it must not be assigned to that function.
/// </summary>
public static class PlatformFunctionRequirements
{
    public static bool SatisfiesProviderDefault(AIModel model) => model.SupportsStreaming;

    public static bool Satisfies(AiCapability capability, AIModel model) => capability switch
    {
        AiCapability.Chat => model.SupportsStreaming,
        AiCapability.ImageGeneration => model.SupportsImageOutput,
        AiCapability.BoundaryVision => model.SupportsVision,
        _ => true,
    };
}
