// Aura layer's rotating RGB gradient - ported from viewer/effects/AnimatedBorderHighlight.ts's
// CONIC_GRADIENT_GLSL (the confirmed site boundary ring's own rotating rainbow, live user
// request 2026-09-07: "apply the RGB alternating colors ... to the aurora layer"). Same four
// colour stops, same technique (four mix() segments picked by step(), avoiding dynamic array
// indexing for GPU/driver compatibility) - kept in sync manually since GLSL has no cross-file
// import mechanism; if the boundary ring's colours are ever retuned, update both.
//
// vUv.x (the sphere's own longitude/azimuth, from THREE.SphereGeometry's own UV layout) stands
// in for AnimatedBorderHighlight's per-fragment angle-around-centroid - both are "how far around
// the shape this fragment is," just computed differently for a flat boundary ring vs. a 3D
// sphere. Multiplies the scrolling aura texture rather than replacing it, so the flame/aura
// silhouette shape is unchanged - only its tint now cycles through the same four colours instead
// of a fixed idle/reactive theme colour.

uniform sampler2D uMap;
uniform float uRotation;
uniform float uOpacity;

varying vec2 vUv;

vec3 conicGradient(float t) {
  t = fract(t) * 4.0;
  vec3 c0 = vec3(1.0, 0.2706, 0.2706); // #ff4545
  vec3 c1 = vec3(0.0, 1.0, 0.6);       // #00ff99
  vec3 c2 = vec3(0.0, 0.4157, 1.0);    // #006aff
  vec3 c3 = vec3(1.0, 0.0, 0.5843);    // #ff0095
  vec3 result = mix(c0, c1, clamp(t, 0.0, 1.0));
  result = mix(result, mix(c1, c2, clamp(t - 1.0, 0.0, 1.0)), step(1.0, t));
  result = mix(result, mix(c2, c3, clamp(t - 2.0, 0.0, 1.0)), step(2.0, t));
  result = mix(result, mix(c3, c0, clamp(t - 3.0, 0.0, 1.0)), step(3.0, t));
  return result;
}

void main() {
  vec4 texel = texture2D(uMap, vUv);
  vec3 color = conicGradient(vUv.x - uRotation);
  gl_FragColor = vec4(texel.rgb * color, texel.a * uOpacity);
}
