// Open flyouts, and whether one was open when the current press began: a click that only dismisses a flyout must not
// collapse the ribbon as well. The press is recorded in the capture phase, before any click handler runs.
let openFlyoutCount = 0
let flyoutOpenAtPointerDown = false

if (typeof document !== 'undefined') {
  document.addEventListener(
    'pointerdown',
    () => {
      flyoutOpenAtPointerDown = openFlyoutCount > 0
    },
    true,
  )
}

export function flyoutOpened(): void {
  openFlyoutCount += 1
}

export function flyoutClosed(): void {
  openFlyoutCount -= 1
}

/** True when a flyout was open as the current press began. */
export function flyoutWasOpenAtPointerDown(): boolean {
  return flyoutOpenAtPointerDown
}
