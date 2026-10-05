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
