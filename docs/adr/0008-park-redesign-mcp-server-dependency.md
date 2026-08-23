# ADR-0008: External Python MCP server (`park-redesign/mcp_server`) for site-analysis geospatial tools

**Status**: Accepted
**Date**: 2026-08-22
**Deciders**: Engineering (SPEC-050 Conversational Park Site Analysis Agent)

## Context

`specs/050-park-site-analysis-agent` wraps the `park-redesign` repository's existing, validated
Python GIS/scoring pipeline (`osmnx`, `rasterio`, `geopandas`, Earth Engine, Gemini-vision) as MCP
tools the Agent Engine can call conversationally. None of these libraries have a .NET equivalent,
and porting the pipeline to C# would both duplicate already-working logic and risk behavior drift
from the known-good `pipeline_results.json` reference output.

Constitution §17 requires an ADR before introducing a new cross-cutting infrastructure dependency —
a second-repository external service, reached only through the existing MCP Tool Engine
(`specs/021`), is exactly that class of decision.

## Decision

Implement the in-scope pipeline stages (site-boundary resolution, Recreation/Social data-layer
collection, Recreation/Social scoring — `specs/050` research.md Decision 4) as a new MCP server
package, `park-redesign/mcp_server/`, using the **official** Python `mcp` SDK (matching this
codebase's own precedent, `specs/021`, of using the official SDK rather than hand-rolling the
protocol) over the Streamable HTTP transport, registered into Ask Lucy through the existing,
unmodified `McpServersController` admin-registration flow. The server wraps the notebooks' modules
verbatim — it does not reimplement their analysis logic.

## Alternatives considered

- **Port the pipeline to C#** — rejected: no equivalent geospatial ecosystem in .NET
  (`osmnx`/`rasterio`/`geopandas` have no direct .NET counterparts), and a reimplementation would
  silently change what the analysis computes, contradicting the goal of migrating the pipeline's
  *interaction model* (notebook → conversational) without changing its *behavior*.
- **Host the Python server inside the `hydra` solution** — rejected: this repository's solution,
  `Directory.Build.props`, and CI are entirely .NET; a Python subtree here would mix build/deploy
  concerns for no benefit, when `park-redesign` already holds every dependency (`.venv`, the
  notebooks, `tools/`) this server needs.
- **stdio transport instead of Streamable HTTP** — rejected: `McpServersController`'s registration
  model assumes a running, network-reachable, health-checked service (endpoint + transport), not a
  locally-spawned subprocess.

## Consequences

- A new, second-repository build/deploy artifact exists (`park-redesign/mcp_server`), with its own
  Python dependency surface and its own outage/versioning story, distinct from `hydra`'s .NET
  release cadence.
- Ask Lucy only ever depends on this server through the existing `IMcpClient`/`McpToolAdapter`
  abstraction (`specs/021`) — zero changes to the Agent Engine or MCP Tool Engine's core mechanisms
  were needed to add it (`specs/050` plan.md Constitution Check).
- Upgrading or replacing the geospatial pipeline later (e.g., a rewrite, a different provider) is a
  change to this one external server and its registered tool set, not to `hydra`'s Application/
  Domain code — the same "swap an `Infrastructure` implementation" property the constitution asks
  every external dependency to have.
- The server's own upstream credentials (mapping/imagery/vision providers) are configured through
  Ask Lucy's existing MCP credential-storage mechanism at registration time (FR-009), not a new
  credential-storage mechanism.
