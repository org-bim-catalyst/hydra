import { create } from 'zustand'

interface ComingSoonState {
  featureLabel: string | null
  show: (label: string) => void
  hide: () => void
}

/** Backs the shared "coming soon" modal (readdy.ai reference): clicking a
 * not-yet-implemented tool action across any workspace control opens the same dialog,
 * naming the feature that isn't built yet, rather than the control itself carrying an
 * inline placeholder message (research.md #6, revised post-launch to match the
 * reference's real-icon-then-modal pattern instead of an empty pill). */
export const useComingSoonStore = create<ComingSoonState>()((set) => ({
  featureLabel: null,
  show: (label) => set({ featureLabel: label }),
  hide: () => set({ featureLabel: null }),
}))
