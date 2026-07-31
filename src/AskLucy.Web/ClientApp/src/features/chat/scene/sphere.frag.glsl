// Draws each point as a soft circular sprite — discarding fragments outside a UV-space
// radius in `gl_PointCoord` — instead of the previous solid-surface Lambertian shading, so
// the sphere reads as a mesh of individual dots (spec 010-lucy-brand-refresh FR-006).
// Colors still mix from uColorIdle toward uColorReactive as vDisplacement (from
// sphere.vert.glsl) grows, same "lights up while deforming" behavior the prior solid
// sphere had — uColorIdle/uColorReactive are now theme-driven (FR-008, dotMeshTheme.ts)
// rather than fixed literals.

uniform vec3 uColorIdle;
uniform vec3 uColorReactive;

varying float vDisplacement;

void main() {
  vec2 fromCenter = gl_PointCoord - vec2(0.5);
  float dist = length(fromCenter);
  if (dist > 0.5) discard;

  float alpha = smoothstep(0.5, 0.3, dist);
  float reactiveMix = smoothstep(0.0, 0.35, abs(vDisplacement));
  vec3 color = mix(uColorIdle, uColorReactive, reactiveMix);

  gl_FragColor = vec4(color, alpha);
}
