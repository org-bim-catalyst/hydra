import { Box, alpha } from '@mui/material'
import type { Theme } from '@mui/material'
import { lazy, Suspense } from 'react'
import { PRESENCE_CARD_SIZE_SX } from './presenceCardSize'
import { usePresenceSphereSettings } from '../hooks/usePresenceSphereSettings'
import type { PresenceSphereLook } from '../scene/sphereConstants'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'

/**
 * The readdy.ai reference's presence-preview card: `w-[25vh] h-[25vh] rounded-lg
 * bg-background-100/50 backdrop-blur-lg border border-background-300/60`.
 *
 * `/50` and `/60` — the card was always meant to be translucent. It was frozen here as the
 * dark literals those tokens resolved to on a light-mode-only preview, which both pinned it
 * dark and, together with the scene's opaque backdrop, made it read as a solid tile. The
 * reference's ramps invert between modes, so the roles are mapped onto the palette instead.
 */
const cardBg = (t: Theme) => alpha(t.palette.background.paper, 0.5)
const cardBorder = (t: Theme) => `1px solid ${alpha(t.palette.divider, 0.6)}`

const SceneBackground = lazy(() =>
  import('../scene/SceneBackground').then((m) => ({ default: m.SceneBackground })),
)

interface AiPresenceCardProps {
  /** Passed through from the single `useVoiceOutput()` instance `ChatPage` already owns
   * and shares with `ConversationView` — NOT a second hook instance here, which would
   * desync the sphere's reaction from actual speech playback (ChatPage.tsx's own
   * existing "lifted above ConversationView" comment explains why). Real per-band FFT data
   * when ElevenLabs is the active provider (useVoiceAnalyzer.ts), or three equal synthesized
   * values from the browser-fallback path (useTextToSpeech.ts) — see either hook's own doc
   * comment. */
  getFrequencyBands: () => FrequencyBands
}

/** FR-023: the existing AI particle-sphere visualization, relocated into its own
 * persistent floating rounded-square card — distinct from the `WorkspaceSurface`
 * (FR-022) and the chat conversation panel — top-left under the studio bar, dark, half the size of the
 * readdy.ai reference's presence-preview card (research.md #7). Always
 * rendered, independent of `workspaceOverlayStore`'s expand/collapse state machine
 * (data-model.md).
 *
 * Reuses `SceneBackground` unchanged, including its existing WebGL-unavailable/render-
 * failure fallback (`SceneErrorBoundary` → a static gradient) — constitution §2.VIII is
 * satisfied without rebuilding that logic. While the scene's own code chunk is still
 * loading, this card shows Lucy's static portrait instead of an empty box (spec.md Edge
 * Cases), matching `AssistantToggleFab`'s prior collapsed-state presentation. */
export function AiPresenceCard({ getFrequencyBands }: AiPresenceCardProps) {
  // The administrator's settings for the sphere (specs/080); the default look until they have loaded.
  const look = usePresenceSphereSettings()
  return <PresenceCardFrame getFrequencyBands={getFrequencyBands} look={look} />
}

/** The card itself, drawn from the given look. The Appearance page's preview wraps its own positioning around this. */
export function PresenceCardFrame({
  getFrequencyBands,
  look,
  floating = true,
}: AiPresenceCardProps & { look: PresenceSphereLook; floating?: boolean }) {
  return (
    <Box
      data-testid="ai-presence-card"
      sx={{
        // Top-left, just under the studio bar: the bottom-left corner is where notices appear, and the card
        // used to cover them. Half its former size, so it stays out of the way of the map. The preview on the
        // Appearance page is not floating: it sits where the page puts it.
        ...(floating
          ? { position: 'absolute', left: { xs: 16, sm: 24 }, top: { xs: 64, sm: 72 } }
          : { position: 'relative' }),
        ...PRESENCE_CARD_SIZE_SX,
        borderRadius: '8px',
        overflow: 'hidden',
        pointerEvents: 'auto',
        bgcolor: cardBg,
        border: cardBorder,
        backdropFilter: 'blur(16px)',
        boxShadow: (t) => (t.palette.mode === 'dark' ? '0 8px 28px rgba(0,0,0,0.35)' : '0 8px 28px rgba(0,0,0,0.18)'),
      }}
    >
      <Suspense
        fallback={
          // Plain surface while the scene chunk loads — no portrait flash (issue doc §Bug A).
          <Box sx={{ position: 'absolute', inset: 0, bgcolor: cardBg }} />
        }
      >
        <SceneBackground getFrequencyBands={getFrequencyBands} look={look} />
      </Suspense>
    </Box>
  )
}
