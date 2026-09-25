using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// The two keys an incident is found by (specs/074 data-model): <see cref="Grouping(OperationalFailureReport)"/>
/// decides which open incident a failure joins (FR-018), <see cref="RootCause"/> which incidents
/// share one cause (FR-026a). Both are SHA-256 lowercase hex over the normalised, <c>|</c>-joined
/// parts. Chats, users, runs and jobs are never part of either, so a burst from many of them stays
/// one incident.
/// </summary>
public static class OperationalFailureKeys
{
    /// <summary><c>engine|provider|model|kind|operation|subjectType|subjectId</c>. The subject label is display only.</summary>
    public static string Grouping(OperationalFailureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Hash(
            report.Engine.ToString(),
            report.ProviderName,
            report.Model,
            report.Kind.ToString(),
            report.Operation,
            report.Subject?.Type,
            report.Subject?.Id.ToString("D"));
    }

    /// <summary>The key of the failover incident a voice recovery belongs to: the Voice engine, never a subject.</summary>
    public static string Grouping(VoiceRecoveryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Hash(
            nameof(OperationalFailureEngine.Voice),
            report.ProviderName,
            report.Model,
            report.Kind.ToString(),
            report.Operation,
            null,
            null);
    }

    /// <summary>Provider kinds: <c>provider|model|kind</c>, so one bad key links chat, voice and embeddings. Others: <c>engine|kind|operation</c>.</summary>
    public static string RootCause(OperationalFailureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return OperationalFailureKinds.IsProviderKind(report.Kind)
            ? Hash(report.ProviderName, report.Model, report.Kind.ToString())
            : Hash(report.Engine.ToString(), report.Kind.ToString(), report.Operation);
    }

    private static string Hash(params string?[] parts)
    {
        var joined = string.Join('|', parts.Select(Normalize));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static string Normalize(string? part) =>
        part is null
            ? string.Empty
            : string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLower(CultureInfo.InvariantCulture);
}
