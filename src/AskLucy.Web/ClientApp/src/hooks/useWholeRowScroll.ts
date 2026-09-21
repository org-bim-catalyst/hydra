import { useEffect, useRef, useState } from 'react'

/**
 * Caps a scrolling table container at a whole number of rows, so its bottom edge always falls
 * on a row boundary and the last visible row is never sliced in half.
 *
 * Bottom padding cannot do this: padding only adds space *after* the final row once you have
 * scrolled all the way down, while the complaint is about the row sitting on the container's
 * bottom edge at any scroll position. Only a container height that is an exact multiple of the
 * row height removes the partial row entirely.
 *
 * Space is measured from the *parent's* content box rather than the container's own height,
 * which would be circular once a cap is applied. A convergence guard (accepting the current cap
 * whenever the available space is within one row of it) stops the feedback loop in the case
 * where the parent does end up sized by this container after all.
 *
 * In jsdom every rect is 0×0 and `ResizeObserver` is usually absent, so this degrades to "no
 * cap" under test — the uncapped layout stays the fallback, never a broken one.
 */
export function useWholeRowScroll<T extends HTMLElement = HTMLDivElement>(
  /** Upper bound for containers capped at a fixed height rather than filling the viewport. */
  ceiling?: number,
) {
  const ref = useRef<T | null>(null)
  const [maxHeight, setMaxHeight] = useState<number | undefined>(undefined)

  useEffect(() => {
    const container = ref.current
    const parent = container?.parentElement
    if (!container || !parent || typeof ResizeObserver === 'undefined') return

    let frame = 0

    const measure = () => {
      const head = container.querySelector<HTMLElement>('thead')
      const rows = container.querySelectorAll<HTMLElement>('tbody tr')
      const rowHeight = rows.length > 0 ? rows[0].getBoundingClientRect().height : 0
      if (rowHeight < 1) {
        setMaxHeight(undefined)
        return
      }
      const headHeight = head?.getBoundingClientRect().height ?? 0

      setMaxHeight((previous) => {
        const room =
          parent.getBoundingClientRect().bottom -
          Number.parseFloat(getComputedStyle(parent).paddingBottom || '0') -
          container.getBoundingClientRect().top
        const available = ceiling === undefined ? room : Math.min(room, ceiling)

        // Already settled: the cap in force still leaves under one row of slack.
        if (previous !== undefined && available >= previous && available < previous + rowHeight) {
          return previous
        }

        const bodyRoom = available - headHeight
        if (bodyRoom < rowHeight) return undefined

        // Everything fits — capping here would only strand rows behind a needless scrollbar.
        if (headHeight + rows.length * rowHeight <= available + 0.5) return undefined

        return headHeight + Math.floor(bodyRoom / rowHeight) * rowHeight
      })
    }

    const schedule = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(measure)
    }

    schedule()
    // The parent drives the available space; the table drives the row count. The container
    // itself is deliberately not observed — that is the edge whose height this hook sets.
    const observer = new ResizeObserver(schedule)
    observer.observe(parent)
    const table = container.querySelector('table')
    if (table) observer.observe(table)
    window.addEventListener('resize', schedule)

    return () => {
      cancelAnimationFrame(frame)
      observer.disconnect()
      window.removeEventListener('resize', schedule)
    }
  }, [ceiling])

  return { ref, maxHeight }
}
