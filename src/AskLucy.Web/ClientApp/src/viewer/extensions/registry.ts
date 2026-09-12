import type { ViewerExtension } from './ViewerExtension'

/** contracts/viewer-extension.md — the catalogue of extensions known to the application, keyed
 * by id. Mirrors `panelTypeRegistry`'s posture exactly (specs/049): fail-fast on a developer
 * mistake (two extensions registered under the same id) in development, never on a runtime
 * condition — an unresolved id at start time is handled gracefully via `resolve()` returning
 * `undefined`, which the loader turns into a visible, non-fatal failure (FR-009) rather than a
 * thrown exception. */
class ViewerExtensionRegistry {
  private readonly extensions = new Map<string, ViewerExtension>()

  register(extension: ViewerExtension): void {
    if (import.meta.env.DEV && this.extensions.has(extension.id)) {
      throw new Error(`Viewer extension "${extension.id}" is already registered.`)
    }
    this.extensions.set(extension.id, extension)
  }

  resolve(id: string): ViewerExtension | undefined {
    return this.extensions.get(id)
  }
}

/** Single module-level registry instance, mirroring `viewerEngineInstance.ts` and
 * `panelTypeRegistry`'s singleton pattern — every built-in extension module registers into this
 * same instance on import. */
export const viewerExtensionRegistry = new ViewerExtensionRegistry()
