using System.Text.Json;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Validates arbitrary, externally-supplied JSON Schema documents against instance data
/// (spec.md FR-025/FR-026, research.md Decision 9). Unlike every native <c>IAgentTool</c>, which
/// hand-validates its own fixed, known shape in C#, MCP tool schemas are supplied by an untrusted
/// external party at runtime — a generic, spec-compliant validator is a genuine new requirement
/// this feature introduces, not a gap in the existing pattern to imitate.
/// </summary>
public interface IJsonSchemaValidator
{
    /// <summary>Returns an empty list when <paramref name="instance"/> both satisfies <paramref name="schema"/> and is within <paramref name="maxSizeBytes"/> (a check independent of schema conformance, FR-051); otherwise one message per violation.</summary>
    IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes);
}
