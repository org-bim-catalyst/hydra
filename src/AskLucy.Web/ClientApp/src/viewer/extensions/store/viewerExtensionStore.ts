import { create } from 'zustand'
import type { Contribution, ExtensionRuntimeState, LifecycleState } from '../ViewerExtension'

interface ViewerExtensionState {
  /** Per-extension runtime state, keyed by id. An id with no entry has never been touched by
   * the loader — distinct from `'not-started'`, which means registered but not yet started. */
  extensions: Record<string, ExtensionRuntimeState>
  /** Every live contribution, from every extension, in the order it was made. Insertion order
   * is what gives toolbar entries their defined, stable order (FR-022) — declared extensions
   * start in declared order, so their contributions land in that same order with no separate
   * bookkeeping needed. */
  contributions: Contribution[]
  setLifecycle: (id: string, lifecycle: LifecycleState, failureReason?: string | null) => void
  setActivation: (id: string, activation: 'active' | 'inactive' | null) => void
  /** FR-017 — recorded independently of lifecycle, since the extension is still running. */
  recordEventFailure: (id: string, message: string) => void
  addContribution: (contribution: Contribution) => void
  /** Bulk withdrawal for one extension — used on stop (FR-015) and on a stop that raced an
   * in-flight start (research D9, FR-010), where "whatever it contributed so far" must go too. */
  removeContributionsFor: (extensionId: string) => void
}

/** data-model.md "Lifecycle State" / "Contribution" — session-scoped only, no `persist`
 * middleware, matching every other viewer store's convention (`viewerEngineStore`,
 * `floatingPanelStore`). Hosts (`ExtensionOverlayHost`, `ExtensionToolbar`,
 * `ExtensionFailureNotice`) read from this store via selector hooks; the loader and context are
 * the only writers. */
export const useViewerExtensionStore = create<ViewerExtensionState>()((set) => ({
  extensions: {},
  contributions: [],

  setLifecycle: (id, lifecycle, failureReason = null) =>
    set((s) => ({
      extensions: {
        ...s.extensions,
        [id]: {
          lifecycle,
          failureReason: lifecycle === 'failed' ? failureReason : null,
          // data-model.md "Lifecycle State" — the active/inactive axis is a second axis, not a
          // sixth state: a stopped (or failed, or not-yet-started) extension is neither, so
          // activation only survives a transition that stays within 'started'. The loader seeds
          // it to 'inactive' with its own `setActivation` call right after a toggleable
          // extension's `setLifecycle(id, 'started')` succeeds.
          activation: lifecycle === 'started' ? (s.extensions[id]?.activation ?? null) : null,
          lastEventError: s.extensions[id]?.lastEventError ?? null,
        },
      },
    })),

  setActivation: (id, activation) =>
    set((s) => {
      const existing = s.extensions[id]
      if (!existing) return s
      return { extensions: { ...s.extensions, [id]: { ...existing, activation } } }
    }),

  recordEventFailure: (id, message) =>
    set((s) => {
      const existing = s.extensions[id]
      if (!existing) return s
      return { extensions: { ...s.extensions, [id]: { ...existing, lastEventError: message } } }
    }),

  addContribution: (contribution) => set((s) => ({ contributions: [...s.contributions, contribution] })),

  removeContributionsFor: (extensionId) =>
    set((s) => ({ contributions: s.contributions.filter((c) => c.extensionId !== extensionId) })),
}))
