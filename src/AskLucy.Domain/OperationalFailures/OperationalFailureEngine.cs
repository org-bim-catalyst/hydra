namespace AskLucy.Domain.OperationalFailures;

/// <summary>The part of the platform a failure happened in (specs/074 FR-007).</summary>
public enum OperationalFailureEngine
{
    Chat,
    AiProvider,
    Voice,
    Embeddings,
    DocumentProcessing,
    ImageGeneration,
    Agent,
    Workflow,
    Mcp,
    BackgroundJob,
    Access,
}
