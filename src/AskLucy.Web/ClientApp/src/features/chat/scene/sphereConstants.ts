/** Shared between ReactiveSphere.tsx and MagmaGlowSphere.tsx so both layers size themselves off
 * the exact same radius. Deliberately its own module, not exported from ReactiveSphere.tsx and
 * imported by MagmaGlowSphere.tsx - that shape is a circular import (ReactiveSphere renders
 * MagmaGlowSphere as a child, MagmaGlowSphere would import a constant back from ReactiveSphere),
 * and depending on module-evaluation order that leaves SPHERE_RADIUS `undefined` in whichever
 * module's top-level constants evaluate first, silently producing NaN sphere radii (confirmed
 * live, 2026-09-07: THREE.BufferGeometry.computeBoundingSphere() NaN errors, nothing rendered). */
export const SPHERE_RADIUS = 1.4

/** The presence card's camera field of view, in degrees. */
export const SPHERE_CAMERA_FOV = 45

/** How much of the card's height the sphere's diameter fills. */
export const SPHERE_CARD_FILL = 0.75

/**
 * How far the camera sits from the sphere for it to fill {@link SPHERE_CARD_FILL} of the card: the visible
 * height at that distance is 2 * d * tan(fov / 2), and the sphere is 2 * radius across.
 */
export const sphereCameraDistance = (fill = SPHERE_CARD_FILL) =>
  (2 * SPHERE_RADIUS) / (fill * 2 * Math.tan((SPHERE_CAMERA_FOV * Math.PI) / 360))

/**
 * specs/080: the presence sphere's three adjustable settings, and the limits an administrator can set them
 * within. Mirrors `PresenceSphereSettings` in the backend (src/AskLucy.Domain/Appearance), which enforces the
 * same limits; the defaults are the look the sphere had before it became adjustable.
 */
export interface PresenceSphereLook {
  /** Multiplies the dots' size: 1 is the size they had before this was adjustable. */
  dotSizeMultiplier: number
  /** The sphere's diameter as a percentage of the card's height. */
  cardFillPercent: number
  /** Whether users may zoom the sphere. */
  zoomEnabled: boolean
}

export const SPHERE_LOOK_LIMITS = {
  dotSizeMultiplier: { min: 0.25, max: 2, step: 0.05 },
  cardFillPercent: { min: 40, max: 95, step: 1 },
} as const

export const DEFAULT_SPHERE_LOOK: PresenceSphereLook = {
  dotSizeMultiplier: 1,
  cardFillPercent: SPHERE_CARD_FILL * 100,
  zoomEnabled: false,
}

/** How far a user can zoom in and out, as multiples of the sphere's normal size. */
export const SPHERE_ZOOM_RANGE = { in: 2, out: 0.25 } as const

/**
 * The camera distances that keep zoom within {@link SPHERE_ZOOM_RANGE} of the sphere's size at `baseDistance`:
 * the sphere's apparent size is inversely proportional to the camera's distance.
 */
export const sphereZoomDistances = (baseDistance: number) => ({
  minDistance: baseDistance / SPHERE_ZOOM_RANGE.in,
  maxDistance: baseDistance / SPHERE_ZOOM_RANGE.out,
})
