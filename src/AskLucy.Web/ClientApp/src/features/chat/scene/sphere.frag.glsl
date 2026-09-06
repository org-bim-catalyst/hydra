// Fragment-stage version of the reference's lighting/fresnel math (Bruno Simon's "Organic
// Sphere", user-supplied source, 2026-09-07) — moved here from the vertex shader (see
// sphere.vert.glsl's header for why) so it interpolates smoothly at this app's lower
// subdivision count instead of faceting like per-vertex (Gouraud) shading would.
//
// Two colored "lights" (idle/reactive theme colors, dotMeshTheme.ts) are combined via a
// fresnel term into a black base, then mixed toward white at the sphere's brightest
// silhouette highlight — the reference's exact formula, unchanged.

uniform vec3 uLightAColor;
uniform vec3 uLightAPosition;
uniform float uLightAIntensity;
uniform vec3 uLightBColor;
uniform vec3 uLightBPosition;
uniform float uLightBIntensity;

uniform float uFresnelOffset;
uniform float uFresnelMultiplier;
uniform float uFresnelPower;

varying vec3 vNormal;
varying vec3 vViewDirection;

void main() {
  vec3 normal = normalize(vNormal);
  vec3 viewDirection = normalize(vViewDirection);

  float fresnel = uFresnelOffset + (1.0 + dot(viewDirection, normal)) * uFresnelMultiplier;
  fresnel = pow(max(0.0, fresnel), uFresnelPower);

  float lightAIntensity = max(0.0, -dot(normal, normalize(-uLightAPosition))) * uLightAIntensity;
  float lightBIntensity = max(0.0, -dot(normal, normalize(-uLightBPosition))) * uLightBIntensity;

  vec3 color = vec3(0.0);
  color = mix(color, uLightAColor, lightAIntensity * fresnel);
  color = mix(color, uLightBColor, lightBIntensity * fresnel);
  color = mix(color, vec3(1.0), clamp(pow(max(0.0, fresnel - 0.8), 3.0), 0.0, 1.0));

  gl_FragColor = vec4(color, 1.0);
}
