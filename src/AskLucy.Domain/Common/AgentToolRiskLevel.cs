namespace AskLucy.Domain.Common;

/// <summary>
/// How dangerous invoking a tool is, and therefore which approval gate it must pass
/// (<c>AgentExecutionOrchestrator</c> requires approval at <see cref="High"/> and
/// <see cref="Critical"/>).
/// <para>
/// Platform vocabulary, not an Agents concept: native tools, MCP tools surfaced from a third-party
/// server, and every conversational capability that declares a risk all use the same four levels,
/// and a DTO-layer copy of it once existed only to avoid importing the Agents namespace. It lives
/// in <c>Common</c> for the same reason <see cref="BaseEntity"/> does — it is shared by every
/// bounded context rather than owned by one.
/// </para>
/// </summary>
public enum AgentToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}
