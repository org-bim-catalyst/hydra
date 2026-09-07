/** Shared between ReactiveSphere.tsx and MagmaGlowSphere.tsx so both layers size themselves off
 * the exact same radius. Deliberately its own module, not exported from ReactiveSphere.tsx and
 * imported by MagmaGlowSphere.tsx - that shape is a circular import (ReactiveSphere renders
 * MagmaGlowSphere as a child, MagmaGlowSphere would import a constant back from ReactiveSphere),
 * and depending on module-evaluation order that leaves SPHERE_RADIUS `undefined` in whichever
 * module's top-level constants evaluate first, silently producing NaN sphere radii (confirmed
 * live, 2026-09-07: THREE.BufferGeometry.computeBoundingSphere() NaN errors, nothing rendered). */
export const SPHERE_RADIUS = 1.4
