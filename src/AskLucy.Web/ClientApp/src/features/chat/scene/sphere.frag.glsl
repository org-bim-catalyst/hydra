// Fragment-stage version of the reference's lighting/fresnel math (Bruno Simon's "Organic
// Sphere", user-supplied source, 2026-09-07) — moved here from the vertex shader (see
// sphere.vert.glsl's header for why) so it interpolates smoothly at this app's lower
// subdivision count instead of faceting like per-vertex (Gouraud) shading would.
//
// Two colored "lights" (a hue-cycling palette, ReactiveSphere.tsx) are combined via a
// fresnel term, then mixed toward white at the sphere's brightest silhouette highlight — the
// reference's formula, with one deliberate tuning change (live user review, 2026-09-07):
//
// The reference starts from a pure black base and uses uFresnelOffset = -1.609, which makes
// any fragment facing the camera close to head-on (dot(viewDirection, normal) ≈ -1) clamp to
// fresnel = 0 — i.e. pure black — lighting only the silhouette rim. That reads as a dramatic
// "glowing orb in a black void" in the reference's full-viewport hero demo, but in this app's
// small card it just looks like a black blob with a thin colored edge. Fixed here with an
// ambient base tint (so the whole surface reads as colored, not just the rim) and a softened
// fresnel offset (so more of the surface — not only the grazing edge — picks up light).
const float AMBIENT_STRENGTH = 0.16;
// Sharper, wider white rim highlight (live user review, 2026-09-07 — pushing further on
// "glass" after real transparency turned out not to work with the current bloom pipeline;
// see below). Lower threshold = the highlight starts kicking in earlier across the fresnel
// falloff, reading as a broader glassy edge-glow rather than a thin line right at the
// silhouette; lower exponent softens/widens that glow instead of a hard cutoff. Paired with
// FRESNEL_POWER raised in ReactiveSphere.tsx for a steeper overall falloff.
const RIM_HIGHLIGHT_THRESHOLD = 0.55;
const RIM_HIGHLIGHT_EXPONENT = 2.5;

// Real see-through transparency (fresnel-driven alpha) was tried here and reverted (live user
// review, 2026-09-07): with SphereBloom.tsx's SelectiveBloom/EffectComposer pipeline active,
// the sphere rendered fully invisible rather than partially transparent — exactly the
// alpha-not-carried-through-postprocessing risk flagged when it was added, confirmed instead of
// theoretical. Back to a flat, opaque alpha; revisit only alongside changes to the bloom
// pipeline itself, not as a shader-only tweak.

uniform vec3 uLightAColor;
uniform vec3 uLightAPosition;
uniform float uLightAIntensity;
uniform vec3 uLightBColor;
uniform vec3 uLightBPosition;
uniform float uLightBIntensity;

uniform float uFresnelOffset;
uniform float uFresnelMultiplier;
uniform float uFresnelPower;

// Blinn-Phong specular highlights. Switched from light-tinted to white (live user review,
// 2026-09-07 — "instead of metal make it glass"): a metal tints its specular by the surface's
// own color; a dielectric (glass, plastic) reflects specular in the *light's* color regardless
// of the surface's own hue — since these lights are effectively white-balanced highlights on a
// colored surface here, white specular is what actually reads as glass/dielectric rather than
// polished metal. Shininess raised and tightened for small, crisp, bright glass-like glints
// rather than the softer metallic sheen the previous (colored, lower-shininess) version had.
uniform float uSpecularShininess;
uniform float uSpecularStrength;

varying vec3 vNormal;
varying vec3 vViewDirection;

void main() {
  vec3 normal = normalize(vNormal);
  vec3 viewDirection = normalize(vViewDirection);
  // Toward the camera, for specular — vViewDirection itself points the other way (surface to
  // camera is what's needed to reflect off, not camera to surface).
  vec3 toCamera = -viewDirection;

  float fresnel = uFresnelOffset + (1.0 + dot(viewDirection, normal)) * uFresnelMultiplier;
  fresnel = pow(max(0.0, fresnel), uFresnelPower);

  vec3 lightADir = normalize(uLightAPosition);
  vec3 lightBDir = normalize(uLightBPosition);
  float lightAIntensity = max(0.0, dot(normal, lightADir)) * uLightAIntensity;
  float lightBIntensity = max(0.0, dot(normal, lightBDir)) * uLightBIntensity;

  vec3 color = mix(uLightAColor, uLightBColor, 0.5) * AMBIENT_STRENGTH;
  color = mix(color, uLightAColor, lightAIntensity * fresnel);
  color = mix(color, uLightBColor, lightBIntensity * fresnel);
  color = mix(color, vec3(1.0), clamp(pow(max(0.0, fresnel - RIM_HIGHLIGHT_THRESHOLD), RIM_HIGHLIGHT_EXPONENT), 0.0, 1.0));

  vec3 halfwayA = normalize(lightADir + toCamera);
  vec3 halfwayB = normalize(lightBDir + toCamera);
  float specularA = pow(max(0.0, dot(normal, halfwayA)), uSpecularShininess);
  float specularB = pow(max(0.0, dot(normal, halfwayB)), uSpecularShininess);
  color += vec3(1.0) * (specularA + specularB) * uSpecularStrength;

  gl_FragColor = vec4(color, 1.0);
}
