/** The overlay surface's floor pass.
 *
 * Content on this layer is drawn first, when the overlay's depth buffer already holds the
 * building footprints (a depth-only mesh), so it is clipped wherever a building stands — the
 * "painted on the ground" look. The depth buffer is then cleared and everything on the default
 * layer draws over the top. Only content that genuinely lies on the ground belongs here.
 */
export const OVERLAY_FLOOR_LAYER = 1
