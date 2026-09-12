import type { PanelTypeDefinition } from './types/panel'

/** contracts/panel-request.md "Live Panel Kind" (specs/049 FR-022/FR-026) — registration is
 * needed only for panels whose content is code rather than data: one that holds continuous
 * state, owns its own drawing surface, or needs values flowing back into it live. Content panels
 * (the vocabulary in `viewer/panels/content/`) need no registration at all; nothing built into
 * this feature registers here — the four built-in kinds this registry used to hold at import time
 * (chart, table, parameters, summary) are now content blocks. A future extension (specs/050)
 * registers a live kind when it starts and withdraws it when it stops, via `unregister`, so
 * stopping an extension leaves nothing behind in this registry. */
class PanelTypeRegistry {
  private readonly definitions = new Map<string, PanelTypeDefinition>()

  register<T>(definition: PanelTypeDefinition<T>): void {
    if (import.meta.env.DEV && this.definitions.has(definition.typeKey)) {
      // Fail-fast on a developer mistake (two types registered under the same key), never on a
      // runtime AI-request condition — an unresolved key at request time is handled gracefully
      // via `resolve()` returning `undefined` instead.
      throw new Error(`Panel type "${definition.typeKey}" is already registered.`)
    }
    this.definitions.set(definition.typeKey, definition as PanelTypeDefinition)
  }

  /** Withdraws a registered live panel kind. A no-op for a key that was never registered — the
   * caller (an extension stopping, specs/050) shouldn't need to track whether it succeeded. */
  unregister(typeKey: string): void {
    this.definitions.delete(typeKey)
  }

  resolve(typeKey: string): PanelTypeDefinition | undefined {
    return this.definitions.get(typeKey)
  }
}

/** Single module-level registry instance, mirroring `viewerEngineInstance.ts`'s singleton
 * pattern — every registered live panel kind registers into this same instance. */
export const panelTypeRegistry = new PanelTypeRegistry()
