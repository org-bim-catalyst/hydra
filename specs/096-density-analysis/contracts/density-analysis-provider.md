# Density Analysis Provider Contract

`IDensityAnalysisProvider` is Application-owned. Implementations accept only validated definitions and execution context, honour cancellation, and return typed complete, partial, empty, failed, or cancelled output.

- Point-count: feature count per area.
- Point-kernel: smoothed feature estimate per area using effective radius.
- Population: people per area, citing population and area inputs.

Providers emit finite non-negative values, valid geometry, canonical units, coverage/exclusion/provenance details, and never narrate to users or manipulate layers. Empty data is not used to represent source or calculation failure.
