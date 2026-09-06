// Fresnel-lit shading for the noise-displaced organic sphere (live user review, 2026-09-06 —
// pivoted from a particle-cloud + additive-blending technique to this continuous, opaque
// surface after repeated GPU-specific breakage: a NaN vertex discarded every point on one
// machine, and — once patched — additive overlap of thousands of points saturated into a
// solid blob on another. An opaque surface has no equivalent failure mode; there's nothing to
// discard or oversaturate. Rim-lighting look inspired by organic-sphere.vercel.app.
//
// Colors mix from uColorIdle toward uColorReactive as vDisplacement (from sphere.vert.glsl)
// grows — the same "lights up while deforming" behavior the particle-cloud version had —
// theme-driven via dotMeshTheme.ts.

uniform vec3 uColorIdle;
uniform vec3 uColorReactive;
uniform float uIntensity;

varying float vDisplacement;
varying vec3 vViewNormal;
varying vec3 vViewDir;

void main() {
  vec3 normal = normalize(vViewNormal);
  vec3 viewDir = normalize(vViewDir);

  // Near 0 facing the camera head-on, near 1 at the sphere's silhouette edge — dims the core
  // and brightens the rim so the surface reads as a lit, three-dimensional orb rather than a
  // flat-shaded disc.
  float fresnel = pow(1.0 - clamp(dot(normal, viewDir), 0.0, 1.0), 2.0);

  float reactiveMix = smoothstep(0.0, 0.35, abs(vDisplacement));
  vec3 baseColor = mix(uColorIdle, uColorReactive, reactiveMix);

  vec3 color = baseColor * (0.3 + fresnel * 1.4) * uIntensity;

  gl_FragColor = vec4(color, 1.0);
}
