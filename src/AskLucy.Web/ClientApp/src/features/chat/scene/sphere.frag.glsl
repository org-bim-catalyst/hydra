// Draws each point as a soft, glowing circular sprite — discarding fragments outside a
// UV-space radius in `gl_PointCoord` — instead of the previous solid-surface Lambertian
// shading, so the sphere reads as a mesh of individual dots (spec 010-lucy-brand-refresh
// FR-006) with a smooth, glow-like edge falloff rather than a hard-edged disc (spec
// 011-particle-sphere-engine FR-002). The full center-to-edge gradient (rather than a flat
// opaque core with only a thin soft rim) is what gives each particle a diffuse "glow" look,
// and is what makes overlapping particles visibly brighten under additive blending
// (FR-003 — set on the material in ReactiveSphere.tsx, not here) instead of just occluding.
// Colors still mix from uColorIdle toward uColorReactive as vDisplacement (from
// sphere.vert.glsl) grows, same "lights up while deforming" behavior the prior solid
// sphere had — uColorIdle/uColorReactive are now theme-driven (FR-008, dotMeshTheme.ts)
// rather than fixed literals.
//
// uIntensity scales the final alpha (spec 011-particle-sphere-engine research.md §2's
// flagged risk, confirmed in manual testing, T008): under additive blending, tens of
// thousands of overlapping particles sum their alpha*color contributions per pixel with no
// upper bound until the framebuffer clamps to white — a point tuned for a sparse, few-
// hundred-particle sphere saturates solid white once reused at "full" tier's much higher
// count. ReactiveSphere.tsx sets uIntensity low for the additive "full" tier and 1.0 for
// "reduced" (normal blending, where overlap doesn't compound), so the sphere still reads as
// individually visible glowing dust instead of a flat, textureless disc.

uniform vec3 uColorIdle;
uniform vec3 uColorReactive;
uniform float uIntensity;

varying float vDisplacement;

void main() {
  vec2 fromCenter = gl_PointCoord - vec2(0.5);
  float dist = length(fromCenter);
  if (dist > 0.5) discard;

  float alpha = smoothstep(0.5, 0.0, dist) * uIntensity;
  float reactiveMix = smoothstep(0.0, 0.35, abs(vDisplacement));
  vec3 color = mix(uColorIdle, uColorReactive, reactiveMix);

  gl_FragColor = vec4(color, alpha);
}
