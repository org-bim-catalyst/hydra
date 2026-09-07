// Inner-glow rim shell for MagmaGlowSphere.tsx, ported from the ics.media "magma effect" demo's
// InGlow.ts. Original TSL: alphaNode = normalView.dot(positionViewDirection).clamp().oneMinus()
// .mul(0.55); colorNode = vec4(color(0x96ecff), alphaNode) - translated to plain GLSL below,
// same formula. uStrength replaces the demo's fixed 0.55 so MagmaGlowSphere.tsx can drive it
// with real voice-reactive intensity (idle vs. speaking) instead of a constant. ASCII-only,
// explicit const types - see sphere.vert.glsl's header for why this matters on this project.

uniform vec3 uColor;
uniform float uStrength;

varying vec3 vViewNormal;
varying vec3 vViewDir;

void main() {
  float facing = clamp(dot(normalize(vViewNormal), normalize(vViewDir)), 0.0, 1.0);
  float alpha = (1.0 - facing) * uStrength;
  gl_FragColor = vec4(uColor, alpha);
}
