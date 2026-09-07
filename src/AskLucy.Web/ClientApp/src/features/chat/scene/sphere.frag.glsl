// Restored 2026-09-07 to the spec 011-particle-sphere-engine design (see sphere.vert.glsl's
// header for the full context of this restore).
//
// Draws each point as a soft, glowing circular sprite - discarding fragments outside a
// UV-space radius in `gl_PointCoord` - so the sphere reads as a mesh of individual dots with a
// smooth, glow-like edge falloff rather than a hard-edged disc. The full center-to-edge
// gradient (rather than a flat opaque core with only a thin soft rim) is what gives each
// particle a diffuse "glow" look, and is what makes overlapping particles visibly brighten
// under additive blending (set on the material in ReactiveSphere.tsx, not here) instead of
// just occluding. Colors mix from uColorIdle toward uColorReactive as vDisplacement (from
// sphere.vert.glsl) grows - uColorIdle/uColorReactive are theme-driven (dotMeshTheme.ts).
//
// uIntensity scales the final alpha: under additive blending, thousands of overlapping
// particles sum their alpha*color contributions per pixel with no upper bound until the
// framebuffer clamps to white - a point tuned for a sparse, few-hundred-particle sphere
// saturates solid white once reused at "full" tier's much higher count (confirmed live,
// NVIDIA RTX 3080). ReactiveSphere.tsx sets uIntensity low for the additive "full" tier and
// 1.0 for "reduced" (normal blending, where overlap doesn't compound).

uniform vec3 uColorIdle;
uniform vec3 uColorReactive;
uniform float uIntensity;

varying float vDisplacement;
varying vec3 vViewNormal;
varying vec3 vViewDir;

void main() {
  vec2 fromCenter = gl_PointCoord - vec2(0.5);
  float dist = length(fromCenter);
  if (dist > 0.5) discard;

  // Fresnel rim term: near 0 for points facing the camera head-on, near 1 for points at the
  // sphere's silhouette edge. Used two ways below - together they cut how much the additive
  // blending pass sums per pixel (the saturation risk noted above, which this rim-weighting
  // is what actually keeps in check, not uIntensity alone) while giving the sphere a lit,
  // three-dimensional rim instead of a flat wash of identical dots.
  float fresnel = pow(1.0 - clamp(dot(normalize(vViewNormal), normalize(vViewDir)), 0.0, 1.0), 2.0);

  float alpha = smoothstep(0.5, 0.0, dist) * uIntensity * mix(0.25, 1.0, fresnel);
  float reactiveMix = smoothstep(0.0, 0.35, abs(vDisplacement));
  vec3 color = mix(uColorIdle, uColorReactive, reactiveMix) * (1.0 + fresnel * 0.8);

  gl_FragColor = vec4(color, alpha);
}
