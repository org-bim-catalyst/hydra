import { Box } from '@mui/material'
import { lazy, Suspense } from 'react'

/** Sampled directly from the readdy.ai reference's own presence-preview card
 * (`getComputedStyle`, not eyeballed): `w-[25vh] h-[25vh] rounded-lg
 * bg-background-100/50 backdrop-blur-lg border border-background-300/60`. */
const CARD_BG = 'oklch(0.18 0.02 280 / 0.5)'
const CARD_BORDER = '1px solid oklch(0.34 0.02 280 / 0.6)'

const SceneBackground = lazy(() =>
  import('../scene/SceneBackground').then((m) => ({ default: m.SceneBackground })),
)

interface AiPresenceCardProps {
  /** Passed through from the single `useVoiceOutput()` instance `ChatPage` already owns
   * and shares with `ConversationView` — NOT a second hook instance here, which would
   * desync the sphere's reaction from actual speech playback (ChatPage.tsx's own
   * existing "lifted above ConversationView" comment explains why). */
  getReactiveIntensity: () => number
}

/** FR-023: the existing AI particle-sphere visualization, relocated into its own
 * persistent floating rounded-square card — distinct from the `WorkspaceSurface`
 * (FR-022) and the chat conversation panel — bottom-left, dark, sized to match the
 * readdy.ai reference's own presence-preview card exactly (research.md #7). Always
 * rendered, independent of `workspaceOverlayStore`'s expand/collapse state machine
 * (data-model.md).
 *
 * Reuses `SceneBackground` unchanged, including its existing WebGL-unavailable/render-
 * failure fallback (`SceneErrorBoundary` → a static gradient) — constitution §2.VIII is
 * satisfied without rebuilding that logic. While the scene's own code chunk is still
 * loading, this card shows Lucy's static portrait instead of an empty box (spec.md Edge
 * Cases), matching `AssistantToggleFab`'s prior collapsed-state presentation. */
export function AiPresenceCard({ getReactiveIntensity }: AiPresenceCardProps) {
  return (
    <Box
      sx={{
        position: 'absolute',
        left: { xs: 16, sm: 24 },
        bottom: { xs: 16, sm: 24 },
        width: 'min(25vh, 280px)',
        height: 'min(25vh, 280px)',
        minWidth: 180,
        minHeight: 180,
        borderRadius: '8px',
        overflow: 'hidden',
        pointerEvents: 'auto',
        bgcolor: CARD_BG,
        border: CARD_BORDER,
        backdropFilter: 'blur(16px)',
        boxShadow: '0 8px 28px rgba(0,0,0,0.35)',
      }}
    >
      <Suspense
        fallback={
          // Dark background while the scene chunk loads — no portrait flash (issue doc §Bug A).
          <Box sx={{ position: 'absolute', inset: 0, bgcolor: CARD_BG }} />
        }
      >
        <SceneBackground getReactiveIntensity={getReactiveIntensity} />
      </Suspense>
    </Box>
  )
}
