# Quickstart: Density Analysis Validation

**Branch**: `096-density-analysis` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Use [data-model.md](data-model.md) and [contracts/](contracts/) for exact types and payloads.

## Prerequisites

- Spatial Analysis Orchestrator and Spatial Query layer transport tests pass.
- `density.analysis`, `DensityAnalysisSpecialist`, a catalog, and a fixture-backed provider are registered.
- An authenticated browser session has a bounded study area or valid map extent.
- Run backend tests from the repository root and ClientApp tests from `src/AskLucy.Web/ClientApp`.

## Scenario 1 — Point-kernel hotspot

1. Use a hospital fixture with a known cluster in Al Khuwair.
2. Ask: “Where are hospitals most concentrated in Al Khuwair using a 1 km radius?”
3. Confirm resolved source, scope, kernel method, radius, cell size/default, unit, coverage, and hotspot are stated.

**Expected**: Within 10 seconds, the known cluster has a greater value/class than surroundings and appears in an independently managed surface layer with accessible legend.

## Scenario 2 — Low coverage

1. Ask: “Show areas of lowest hospital coverage in Al Khuwair.”
2. Provide missing details or accept disclosed defaults.

**Expected**: Lowest cells are shown and described as concentration patterns, not proof of service need; limitations are visible.

## Scenario 3 — Population density

1. Load a catalogued fixture with population values and areas.
2. Ask: “Show population density in Al Khuwair.”

**Expected**: Values use people per declared area. Missing/zero source records are disclosed when material. The same request against a point-only hospital source fails clearly and creates no surface.

## Scenario 4 — Display versus calculation changes

1. Create a layer and change opacity, palette, visible range, and display classification.
2. Record a selected cell's value, then change radius or cell size.

**Expected**: Display-only edits preserve the recorded value. Calculation edits create a separately named/recomputed result carrying new effective parameters and audit entry.

## Scenario 5 — Layer lifecycle

1. Create two density layers with different settings.
2. Hide one, remove the other, switch 2D/3D, and clear the remaining analysis.

**Expected**: Only the requested layer changes; map and legend remain synchronized across the view switch.

## Scenario 6 — Failure visibility

Exercise invalid size/radius, incompatible method/source, empty data, partial coverage, timeout, cancellation, malformed payload, and forced map render failure.

**Expected**: Each condition produces user-visible explanation and retry/re-run guidance when applicable. No stale layer, console-only error, unhandled rejection, or empty-success surface is accepted.

## Scenario 7 — Audit verification

Run Scenario 1 and inspect the structured log sink.

**Expected**: The audit record includes user/correlation IDs, resolved inputs, requested/effective parameters, coverage, duration, result summary/provenance, and terminal outcome.
