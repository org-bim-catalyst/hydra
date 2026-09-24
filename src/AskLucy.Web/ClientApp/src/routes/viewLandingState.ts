/** Navigation state that lets a signed-in visitor see the landing page instead of being sent
 * back to the Studio (`PublicOnlyRoute`). The Studio's Home button passes it. */
export const VIEW_LANDING_STATE = { viewLanding: true } as const

/** Navigation state the landing page's links set, so a public page opened from there (Terms,
 * Privacy) returns to the landing page rather than the Studio (`AppShell`'s home link). */
export const FROM_LANDING_STATE = { from: 'landing' } as const

export function isFromLanding(state: unknown): boolean {
  return (state as Partial<typeof FROM_LANDING_STATE> | null)?.from === FROM_LANDING_STATE.from
}
