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

uniform vec3 uLightAColor;
uniform vec3 uLightAPosition;
uniform float uLightAIntensity;
uniform vec3 uLightBColor;
uniform vec3 uLightBPosition;
uniform float uLightBIntensity;

uniform float uFresnelOffset;
uniform float uFresnelMultiplier;
uniform float uFresnelPower;

// Blinn-Phong specular highlights, tinted by each light's own color rather than white — a
// dielectric (plastic, skin) reflects specular highlights in the light's color regardless of
// the surface's own hue; a metal tints its specular by the *surface* color instead, but since
// this sphere's own "surface color" already comes from these same two lights (there's no
// separate base albedo to tint by), tinting by light color reads the same way and is what
// actually gives the "polished metal catching a colored light" look requested (live user
// review, 2026-09-07) — small, tight, view-angle-dependent glints on top of the existing
// diffuse+fresnel shading, rather than changing that shading itself.
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
  color = mix(color, vec3(1.0), clamp(pow(max(0.0, fresnel - 0.8), 3.0), 0.0, 1.0));

  vec3 halfwayA = normalize(lightADir + toCamera);
  vec3 halfwayB = normalize(lightBDir + toCamera);
  float specularA = pow(max(0.0, dot(normal, halfwayA)), uSpecularShininess);
  float specularB = pow(max(0.0, dot(normal, halfwayB)), uSpecularShininess);
  color += (uLightAColor * specularA + uLightBColor * specularB) * uSpecularStrength;

  gl_FragColor = vec4(color, 1.0);
}
