import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ContributionErrorBoundary } from './ContributionErrorBoundary'

/** specs/073 contract X2–X7 — renders every `hudItem` contribution into the studio's top-left
 * HUD row, after the host's own items (Home, project title, weather). Renders *from the store*,
 * like `ExtensionOverlayHost`, so a contribution made before this host mounts appears exactly
 * like one made after (X2). Returns a fragment rather than a positioned wrapper, so each item is
 * a direct flex child of the row and the row's gap and wrapping apply to it individually (X6).
 * Each item renders inside its own `ContributionErrorBoundary`, so one throwing item can't take
 * down the row or the `WorkspaceOverlay` around it (X7). */
export function ExtensionHudItemHost() {
  const contributions = useViewerExtensionStore((s) => s.contributions)
  const hudItems = contributions.filter((c) => c.kind === 'hudItem')

  const seen = new Map<string, number>()

  return (
    <>
      {hudItems.map((contribution) => {
        const ordinal = seen.get(contribution.extensionId) ?? 0
        seen.set(contribution.extensionId, ordinal + 1)
        return (
          <ContributionErrorBoundary key={`${contribution.extensionId}:${ordinal}`} extensionId={contribution.extensionId}>
            <contribution.component />
          </ContributionErrorBoundary>
        )
      })}
    </>
  )
}
