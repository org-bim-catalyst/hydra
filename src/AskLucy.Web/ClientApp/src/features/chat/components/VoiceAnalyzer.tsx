import { Box } from '@mui/material'
import { useEffect, useRef } from 'react'
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'

export type VoiceAnalyzerState = 'idle' | 'processing' | 'speaking' | 'listening'

export interface VoiceAnalyzerProps {
  state: VoiceAnalyzerState
  /** Ref-based, read every animation frame — never drives React state per frame
   * (specs/026-floating-chat-assistant research.md #3). */
  getIntensity: () => number
}

const BAR_COUNT = 5
const MIN_SCALE = 0.14

/** FR-004: the Collapsed-state vertical analyzer — a small bar-stack whose animation
 * pattern and color distinguish Idle, Processing (synthetic pulse — nothing to measure
 * yet), and Speaking/Listening (driven by real `getIntensity()` amplitude), polled via
 * `requestAnimationFrame` and applied directly to bar `transform`s (never React state)
 * to stay cheap at 60fps (constitution §15). */
export function VoiceAnalyzer({ state, getIntensity }: VoiceAnalyzerProps) {
  const barRefs = useRef<(HTMLDivElement | null)[]>([])
  const prefersReducedMotion = usePrefersReducedMotion()

  useEffect(() => {
    if (prefersReducedMotion) {
      // Reduced motion: a single static baseline scale per state, no per-frame animation.
      const staticScale = state === 'idle' ? MIN_SCALE : 0.6
      barRefs.current.forEach((bar) => {
        if (bar) bar.style.transform = `scaleY(${staticScale})`
      })
      return
    }

    let frame: number
    let elapsed = 0
    const tick = () => {
      elapsed += 1
      barRefs.current.forEach((bar, index) => {
        if (!bar) return
        let scale: number
        if (state === 'speaking' || state === 'listening') {
          const intensity = getIntensity()
          // Slight per-bar phase offset so the stack doesn't move as one rigid block.
          const wobble = 1 - Math.abs(index - (BAR_COUNT - 1) / 2) * 0.08
          scale = Math.max(MIN_SCALE, Math.min(1, intensity * wobble))
        } else if (state === 'processing') {
          const phase = elapsed / 12 + index * 0.6
          scale = MIN_SCALE + (Math.sin(phase) * 0.5 + 0.5) * 0.55
        } else {
          scale = MIN_SCALE
        }
        bar.style.transform = `scaleY(${scale})`
      })
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [state, getIntensity, prefersReducedMotion])

  const activeColor =
    state === 'listening'
      ? 'secondary.main'
      : state === 'speaking'
        ? 'primary.main'
        : CIRCULAR_ACTION_CHROME.icon

  return (
    <Box
      role="img"
      aria-label={`Voice status: ${state}`}
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 0.5,
        height: 56,
        width: '100%',
      }}
    >
      {Array.from({ length: BAR_COUNT }).map((_, index) => (
        <Box
          key={index}
          ref={(el: HTMLDivElement | null) => {
            barRefs.current[index] = el
          }}
          sx={{
            width: 3,
            height: '100%',
            borderRadius: 999,
            bgcolor: activeColor,
            opacity: state === 'idle' ? 0.35 : 0.9,
            transform: `scaleY(${MIN_SCALE})`,
            transformOrigin: 'center',
            transition: (t) => (state === 'idle' ? t.transitions.create(['opacity']) : 'none'),
          }}
        />
      ))}
    </Box>
  )
}
