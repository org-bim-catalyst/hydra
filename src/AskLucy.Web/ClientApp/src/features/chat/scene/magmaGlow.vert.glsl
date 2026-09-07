// Inner-glow rim shell for MagmaGlowSphere.tsx, ported from the ics.media "magma effect" demo's
// InGlow.ts (TSL/WebGPU original: normalView.dot(positionViewDirection).clamp().oneMinus()).
// This is a plain mesh, unlike sphere.vert.glsl's point cloud, so no per-vertex displacement or
// noise is needed here - just the same view-space normal/view-direction fresnel inputs
// sphere.vert.glsl already computes, reused rather than reinvented (same rim-lighting math, a
// different geometry). ASCII-only, explicit const types - see sphere.vert.glsl's header for why.

varying vec3 vViewNormal;
varying vec3 vViewDir;

void main() {
  vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
  gl_Position = projectionMatrix * mvPosition;

  vViewNormal = normalize(normalMatrix * normal);
  vViewDir = normalize(-mvPosition.xyz);
}
