// Aura layer's rotating RGB gradient - MagmaGlowSphere.tsx, live user request 2026-09-07: reuse
// the same rotating conic-gradient technique already used for the confirmed site boundary ring
// on the map (viewer/effects/AnimatedBorderHighlight.ts's CONIC_GRADIENT_GLSL). Passes UV through
// unchanged - both the scrolling aura texture sample and the gradient's angle input (its own
// longitude/azimuth around the sphere) read from it in magmaAura.frag.glsl.

varying vec2 vUv;

void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
