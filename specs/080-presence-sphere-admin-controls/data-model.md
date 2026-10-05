# Data Model: Presence Sphere Admin Controls

## PresenceSphereSettings (table `PresenceSphereSettings`, exactly one row)

Extends `BaseEntity` (Id, CreatedAtUtc/By, ModifiedAtUtc/By, RowVersion; the soft-delete fields are unused). The row key is a fixed `SingletonId`.

| Field | Type | Range | Default | Notes |
|---|---|---|---|---|
| `DotSizeMultiplier` | decimal(4,2) | 0.25 to 2.00 | 1.00 | Multiplies today's dot size |
| `CardFillPercent` | int | 40 to 95 | 75 | Sphere diameter as a % of the card height |
| `ZoomEnabled` | bit | n/a | false | When true, zoom is limited to 0.25x to 2x of normal |
| `ModifiedBy` / `ModifiedAtUtc` | from base | | | "Last changed by/at" on the page; null until the first save |

### Rules
- No row means defaults. A read never creates the row; the first save creates it (upsert on `SingletonId`).
- `Update(dotSize, fillPercent, zoomEnabled, actor, utcNow)` rejects out-of-range values with a domain error. The Application validator uses the same constants, so the limits exist once.
- No state machine and no history table (the change is logged, see research D9).

### Not stored
Per-user overrides, a zoom level, glow, colours, dot count, rotation speed, and the card's size and position.

## Migration
`AddPresenceSphereSettings` creates the table. No seed row (defaults apply when absent). Apply by hand to the shared persistence test database.
