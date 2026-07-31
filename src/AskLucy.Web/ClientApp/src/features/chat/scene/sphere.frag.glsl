// Simple directional-light shading, tinted from the idle color toward the reactive
// color as vDisplacement (from sphere.vert.glsl) grows — so the sphere visibly "lights
// up" while it's deforming in response to voice output, not just moving.

uniform vec3 uColorIdle;
uniform vec3 uColorReactive;

varying vec3 vNormal;
varying float vDisplacement;

void main() {
  float diffuse = max(dot(normalize(vNormal), normalize(vec3(0.35, 0.55, 1.0))), 0.0);
  float reactiveMix = smoothstep(0.0, 0.35, abs(vDisplacement));
  vec3 baseColor = mix(uColorIdle, uColorReactive, reactiveMix);
  vec3 shaded = baseColor * (0.45 + 0.65 * diffuse);

  gl_FragColor = vec4(shaded, 1.0);
}
