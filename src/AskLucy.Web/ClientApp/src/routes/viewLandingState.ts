/** Navigation state that lets a signed-in visitor see the landing page instead of being sent
 * back to the Studio (`PublicOnlyRoute`). The Studio's Home button passes it. */
export const VIEW_LANDING_STATE = { viewLanding: true } as const

/** Navigation state the landing page's links set, so a public page opened from there (Terms,
 * Privacy) returns to the landing page rather than the Studio (`AppShell`'s home link). */
export const FROM_LANDING_STATE = { from: 'landing' } as const

export function isFromLanding(state: unknown): boolean {
  return (state as Partial<typeof FROM_LANDING_STATE> | null)?.from === FROM_LANDING_STATE.from
}

/** Navigation state `NotificationPopover`'s "View all" link sets, so `/notifications`'s
 * `AppShell` home link returns to wherever it was opened from (Studio, an admin page, Settings,
 * ...) instead of always defaulting to the Studio. */
export function fromPathState(pathname: string): { from: string } {
  return { from: pathname }
}

export function fromPath(state: unknown): string | null {
  const from = (state as { from?: unknown } | null)?.from
  return typeof from === 'string' ? from : null
}
