import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'

interface ContributionErrorBoundaryProps {
  extensionId: string
  children: ReactNode
}

interface ContributionErrorBoundaryState {
  failed: boolean
}

/** specs/073 research D3a, contract X7 — contains a render throw to the one contributed component
 * it wraps. Without it, a throwing HUD item would take down the whole `WorkspaceOverlay` (chat,
 * account menu, every control), and a throwing overlay the whole viewer. The failure is recorded
 * through the store's existing lifecycle path, so `ExtensionFailureNotice` shows it exactly as it
 * shows a failed start (constitution §2.VIII — never dropped silently); only this item then
 * renders `null`. Adds no DOM of its own, so the HUD row's gap and wrapping still apply to each
 * item individually (X6). Mirrors `SceneErrorBoundary` (`features/chat/scene/SceneBackground.tsx`). */
export class ContributionErrorBoundary extends Component<ContributionErrorBoundaryProps, ContributionErrorBoundaryState> {
  state: ContributionErrorBoundaryState = { failed: false }

  static getDerivedStateFromError(): ContributionErrorBoundaryState {
    return { failed: true }
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    const message = error instanceof Error ? error.message : String(error)
    console.error(`Extension "${this.props.extensionId}" failed to render a contribution`, error, info.componentStack)
    useViewerExtensionStore.getState().setLifecycle(this.props.extensionId, 'failed', `Render failed: ${message}`)
  }

  render() {
    return this.state.failed ? null : this.props.children
  }
}
