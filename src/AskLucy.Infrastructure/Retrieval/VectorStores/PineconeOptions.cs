namespace AskLucy.Infrastructure.Retrieval.VectorStores;

/// <summary>Configuration for <see cref="PineconeVectorStore"/> (ADR-0007).</summary>
public sealed class PineconeOptions
{
    public const string SectionName = "Pinecone";

    public required string ApiKey { get; init; }

    /// <summary>The index-specific data-plane host (e.g. <c>https://my-index-xxxxx.svc.us-east-1-aws.pinecone.io</c>), not <c>api.pinecone.io</c> (the control-plane host used only to create/describe indexes).</summary>
    public required string IndexHost { get; init; }

    /// <summary>Pinecone's data-plane REST API version header value — verify against current docs before shipping; this vendor periodically bumps the required version string.</summary>
    public string ApiVersion { get; init; } = "2025-10";
}
