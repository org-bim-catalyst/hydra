namespace AskLucy.Infrastructure.KnowledgeBases;

/// <summary>Governs how often <see cref="KnowledgeBasePurgeHostedService"/> sweeps for knowledge bases past their 30-day purge schedule (FR-036).</summary>
public sealed class KnowledgeBasePurgeOptions
{
    public const string SectionName = "KnowledgeBasePurge";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);
}
