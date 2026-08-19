import type { PanelTypeDefinition } from './types/panel'

/** contracts/panel-type-registry.md — the extensibility seam (spec FR-001/FR-015, Clarifications
 * Q1). A developer registers a new panel type once, here; nothing else in the panel framework
 * (`floatingPanelStore`, `FloatingPanel.tsx`, `PanelHub`) ever needs to change to support it. */
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

  resolve(typeKey: string): PanelTypeDefinition | undefined {
    return this.definitions.get(typeKey)
  }
}

/** Single module-level registry instance, mirroring `viewerEngineInstance.ts`'s singleton
 * pattern — every built-in panel type module registers into this same instance on import. */
export const panelTypeRegistry = new PanelTypeRegistry()
