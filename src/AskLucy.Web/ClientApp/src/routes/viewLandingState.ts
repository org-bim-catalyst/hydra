/** Navigation state that lets a signed-in visitor see the landing page instead of being sent
 * back to the Studio (`PublicOnlyRoute`). The Studio's Home button passes it. */
export const VIEW_LANDING_STATE = { viewLanding: true } as const
