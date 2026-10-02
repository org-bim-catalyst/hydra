import { useEffect, useRef, useState } from 'react'

/** How often the level is sampled while active — fast enough to look live, slow enough to not thrash renders. */
const POLL_INTERVAL_MS = 50

/** Polls `readLevel` via `requestAnimationFrame` while `active`; reports 0 the rest of the time. */
export function useLevelPolling(active: boolean, readLevel: () => number): number {
  const [level, setLevel] = useState(0)
  const frameRef = useRef<number | null>(null)

  useEffect(() => {
    if (!active) return

    let lastSample = 0
    const tick = (time: number) => {
      if (time - lastSample > POLL_INTERVAL_MS) {
        setLevel(readLevel())
        lastSample = time
      }
      frameRef.current = requestAnimationFrame(tick)
    }
    frameRef.current = requestAnimationFrame(tick)

    return () => {
      if (frameRef.current !== null) cancelAnimationFrame(frameRef.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `readLevel` reads a ref internally; it need not restart the loop.
  }, [active])

  // Reported 0 whenever inactive, regardless of the last sampled value, without a setState call
  // in the effect above (that would trip react-hooks/set-state-in-effect's cascading-render rule).
  return active ? level : 0
}
