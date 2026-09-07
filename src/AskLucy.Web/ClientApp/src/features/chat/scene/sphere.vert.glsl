// Redesigned 2026-09-07 after a full day of live-tested lessons on this exact sphere (see git
// history on this file and sphere.frag.glsl for the incidents themselves). This version
// combines the two things that turned out to matter most:
//
// 1. Two-stage noise (a distortion pre-pass that warps the sample point, then a displacement
//    pass sampled at that warped point) reads as noticeably more organic than a single noise
//    call scaled by one number - this is Bruno Simon's "Organic Sphere" reference technique,
//    and the version of this file that flattened it down to one call (voltviz's simpler
//    GlowSphere structure, tried as an explicit A/B test) read as visually flatter/more
//    mechanical once compared side by side.
// 2. Real, separate per-band audio data (useVoiceAnalyzer.ts's Web Audio AnalyserNode) drives
//    the two stages independently - low frequencies feed the distortion pass, high frequencies
//    feed the displacement pass - rather than collapsing everything into one scalar (this
//    file's immediately preceding version) or a four-variable independently-eased system with
//    its own state machine (an earlier version, harder to reason about for the size of benefit
//    it gave). No extra JS-side smoothing beyond the AnalyserNode's own built-in
//    smoothingTimeConstant (0.8, the Web Audio default) - kept simple deliberately.
//
// A permanent idle baseline is added to both passes so the sphere never goes fully flat/smooth
// at silence (confirmed live: that reads as "dead" rather than "calm").
//
// Two hard lessons from today are non-negotiable going forward, called out explicitly because
// nothing in this project's build (tsc/eslint) checks GLSL syntax - errors here only surface at
// runtime, in a browser's WebGL console, on whatever machine happens to load it:
// - GLSL has no type inference. Every `const` needs an explicit type (`const float x = ...;`,
//   never bare `const x = ...;`) - a missing type here silently fails shader compilation with
//   no visual clue beyond "the sphere stopped rendering".
// - Only ASCII characters in this file. A non-ASCII character (em dash, curly quote, degree
//   sign) risks silent mojibake if whatever serves the built JS bundle doesn't declare
//   charset=utf-8 correctly - confirmed happening on this project's production host.
//
// Still estimates each vertex's post-displacement normal from two neighboring tangent-plane
// samples - there's no analytic derivative of the noise function to differentiate for a normal.
// Still shades per-fragment, not per-vertex (sphere.frag.glsl) - this app uses far fewer
// subdivisions than a full-viewport hero demo would, where per-vertex lighting would visibly facet.

#define M_PI 3.1415926535897932384626433832795

uniform vec2 uSubdivision;
uniform float uDistortionFrequency;
uniform float uDistortionLevel;
uniform float uDisplacementFrequency;
uniform float uDisplacementLevel;
uniform float uTime;

varying vec3 vNormal;
varying vec3 vViewDirection;

// Classic Perlin 4D Noise by Stefan Gustavson (webgl-noise, MIT license) - inlined here since
// this project's Vite build has no glslify loader (the reference source uses one).
vec4 permute(vec4 x) { return mod(((x * 34.0) + 1.0) * x, 289.0); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
vec4 fade(vec4 t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

float perlin4d(vec4 P) {
  vec4 Pi0 = floor(P);
  vec4 Pi1 = Pi0 + 1.0;
  Pi0 = mod(Pi0, 289.0);
  Pi1 = mod(Pi1, 289.0);
  vec4 Pf0 = fract(P);
  vec4 Pf1 = Pf0 - 1.0;
  vec4 ix = vec4(Pi0.x, Pi1.x, Pi0.x, Pi1.x);
  vec4 iy = vec4(Pi0.yy, Pi1.yy);
  vec4 iz0 = vec4(Pi0.zzzz);
  vec4 iz1 = vec4(Pi1.zzzz);
  vec4 iw0 = vec4(Pi0.wwww);
  vec4 iw1 = vec4(Pi1.wwww);

  vec4 ixy = permute(permute(ix) + iy);
  vec4 ixy0 = permute(ixy + iz0);
  vec4 ixy1 = permute(ixy + iz1);
  vec4 ixy00 = permute(ixy0 + iw0);
  vec4 ixy01 = permute(ixy0 + iw1);
  vec4 ixy10 = permute(ixy1 + iw0);
  vec4 ixy11 = permute(ixy1 + iw1);

  vec4 gx00 = ixy00 / 7.0;
  vec4 gy00 = floor(gx00) / 7.0;
  vec4 gz00 = floor(gy00) / 6.0;
  gx00 = fract(gx00) - 0.5;
  gy00 = fract(gy00) - 0.5;
  gz00 = fract(gz00) - 0.5;
  vec4 gw00 = vec4(0.75) - abs(gx00) - abs(gy00) - abs(gz00);
  vec4 sw00 = step(gw00, vec4(0.0));
  gx00 -= sw00 * (step(0.0, gx00) - 0.5);
  gy00 -= sw00 * (step(0.0, gy00) - 0.5);

  vec4 gx01 = ixy01 / 7.0;
  vec4 gy01 = floor(gx01) / 7.0;
  vec4 gz01 = floor(gy01) / 6.0;
  gx01 = fract(gx01) - 0.5;
  gy01 = fract(gy01) - 0.5;
  gz01 = fract(gz01) - 0.5;
  vec4 gw01 = vec4(0.75) - abs(gx01) - abs(gy01) - abs(gz01);
  vec4 sw01 = step(gw01, vec4(0.0));
  gx01 -= sw01 * (step(0.0, gx01) - 0.5);
  gy01 -= sw01 * (step(0.0, gy01) - 0.5);

  vec4 gx10 = ixy10 / 7.0;
  vec4 gy10 = floor(gx10) / 7.0;
  vec4 gz10 = floor(gy10) / 6.0;
  gx10 = fract(gx10) - 0.5;
  gy10 = fract(gy10) - 0.5;
  gz10 = fract(gz10) - 0.5;
  vec4 gw10 = vec4(0.75) - abs(gx10) - abs(gy10) - abs(gz10);
  vec4 sw10 = step(gw10, vec4(0.0));
  gx10 -= sw10 * (step(0.0, gx10) - 0.5);
  gy10 -= sw10 * (step(0.0, gy10) - 0.5);

  vec4 gx11 = ixy11 / 7.0;
  vec4 gy11 = floor(gx11) / 7.0;
  vec4 gz11 = floor(gy11) / 6.0;
  gx11 = fract(gx11) - 0.5;
  gy11 = fract(gy11) - 0.5;
  gz11 = fract(gz11) - 0.5;
  vec4 gw11 = vec4(0.75) - abs(gx11) - abs(gy11) - abs(gz11);
  vec4 sw11 = step(gw11, vec4(0.0));
  gx11 -= sw11 * (step(0.0, gx11) - 0.5);
  gy11 -= sw11 * (step(0.0, gy11) - 0.5);

  vec4 g0000 = vec4(gx00.x, gy00.x, gz00.x, gw00.x);
  vec4 g1000 = vec4(gx00.y, gy00.y, gz00.y, gw00.y);
  vec4 g0100 = vec4(gx00.z, gy00.z, gz00.z, gw00.z);
  vec4 g1100 = vec4(gx00.w, gy00.w, gz00.w, gw00.w);
  vec4 g0010 = vec4(gx10.x, gy10.x, gz10.x, gw10.x);
  vec4 g1010 = vec4(gx10.y, gy10.y, gz10.y, gw10.y);
  vec4 g0110 = vec4(gx10.z, gy10.z, gz10.z, gw10.z);
  vec4 g1110 = vec4(gx10.w, gy10.w, gz10.w, gw10.w);
  vec4 g0001 = vec4(gx01.x, gy01.x, gz01.x, gw01.x);
  vec4 g1001 = vec4(gx01.y, gy01.y, gz01.y, gw01.y);
  vec4 g0101 = vec4(gx01.z, gy01.z, gz01.z, gw01.z);
  vec4 g1101 = vec4(gx01.w, gy01.w, gz01.w, gw01.w);
  vec4 g0011 = vec4(gx11.x, gy11.x, gz11.x, gw11.x);
  vec4 g1011 = vec4(gx11.y, gy11.y, gz11.y, gw11.y);
  vec4 g0111 = vec4(gx11.z, gy11.z, gz11.z, gw11.z);
  vec4 g1111 = vec4(gx11.w, gy11.w, gz11.w, gw11.w);

  vec4 norm00 = taylorInvSqrt(vec4(dot(g0000, g0000), dot(g0100, g0100), dot(g1000, g1000), dot(g1100, g1100)));
  g0000 *= norm00.x;
  g0100 *= norm00.y;
  g1000 *= norm00.z;
  g1100 *= norm00.w;

  vec4 norm01 = taylorInvSqrt(vec4(dot(g0001, g0001), dot(g0101, g0101), dot(g1001, g1001), dot(g1101, g1101)));
  g0001 *= norm01.x;
  g0101 *= norm01.y;
  g1001 *= norm01.z;
  g1101 *= norm01.w;

  vec4 norm10 = taylorInvSqrt(vec4(dot(g0010, g0010), dot(g0110, g0110), dot(g1010, g1010), dot(g1110, g1110)));
  g0010 *= norm10.x;
  g0110 *= norm10.y;
  g1010 *= norm10.z;
  g1110 *= norm10.w;

  vec4 norm11 = taylorInvSqrt(vec4(dot(g0011, g0011), dot(g0111, g0111), dot(g1011, g1011), dot(g1111, g1111)));
  g0011 *= norm11.x;
  g0111 *= norm11.y;
  g1011 *= norm11.z;
  g1111 *= norm11.w;

  float n0000 = dot(g0000, Pf0);
  float n1000 = dot(g1000, vec4(Pf1.x, Pf0.yzw));
  float n0100 = dot(g0100, vec4(Pf0.x, Pf1.y, Pf0.zw));
  float n1100 = dot(g1100, vec4(Pf1.xy, Pf0.zw));
  float n0010 = dot(g0010, vec4(Pf0.xy, Pf1.z, Pf0.w));
  float n1010 = dot(g1010, vec4(Pf1.x, Pf0.y, Pf1.z, Pf0.w));
  float n0110 = dot(g0110, vec4(Pf0.x, Pf1.yz, Pf0.w));
  float n1110 = dot(g1110, vec4(Pf1.xyz, Pf0.w));
  float n0001 = dot(g0001, vec4(Pf0.xyz, Pf1.w));
  float n1001 = dot(g1001, vec4(Pf1.x, Pf0.yz, Pf1.w));
  float n0101 = dot(g0101, vec4(Pf0.x, Pf1.y, Pf0.z, Pf1.w));
  float n1101 = dot(g1101, vec4(Pf1.xy, Pf0.z, Pf1.w));
  float n0011 = dot(g0011, vec4(Pf0.xy, Pf1.zw));
  float n1011 = dot(g1011, vec4(Pf1.x, Pf0.y, Pf1.zw));
  float n0111 = dot(g0111, vec4(Pf0.x, Pf1.yzw));
  float n1111 = dot(g1111, Pf1);

  vec4 fade_xyzw = fade(Pf0);
  vec4 n_0w = mix(vec4(n0000, n1000, n0100, n1100), vec4(n0001, n1001, n0101, n1101), fade_xyzw.w);
  vec4 n_1w = mix(vec4(n0010, n1010, n0110, n1110), vec4(n0011, n1011, n0111, n1111), fade_xyzw.w);
  vec4 n_zw = mix(n_0w, n_1w, fade_xyzw.z);
  vec2 n_yzw = mix(n_zw.xy, n_zw.zw, fade_xyzw.y);
  float n_xyzw = mix(n_yzw.x, n_yzw.y, fade_xyzw.x);
  return 2.2 * n_xyzw;
}

// Diagnosed on an NVIDIA RTX 3080 (ANGLE D3D11 backend): this noise function can evaluate to
// NaN on some GPU/driver combinations even though the identical GLSL runs correctly elsewhere.
// Guarded at the call site rather than inside perlin4d() itself, since that function is
// third-party and used at two different call sites below.
float safePerlin4d(vec4 p) {
  float n = perlin4d(p);
  return isnan(n) ? 0.0 : n;
}

// Two-stage displacement: the distortion pass warps _position before the displacement pass
// samples noise there, which is what gives this technique its layered, organic look rather
// than a single flat bump field. uDistortionLevel/uDisplacementLevel are set every frame from
// real, separate FFT bands (ReactiveSphere.tsx) - low frequencies warp the sample point, high
// frequencies scale the final radial bump - rather than one collapsed loudness number.
vec3 getDisplacedPosition(vec3 _position) {
  vec3 distortedPosition = _position;
  distortedPosition += safePerlin4d(vec4(distortedPosition * uDistortionFrequency + uTime, uTime)) * uDistortionLevel;

  float displacementNoise = safePerlin4d(vec4(distortedPosition * uDisplacementFrequency + uTime, uTime));

  vec3 displacedPosition = _position;
  displacedPosition += normalize(_position) * displacementNoise * uDisplacementLevel;

  return displacedPosition;
}

void main() {
  vec3 displacedPosition = getDisplacedPosition(position);

  vec3 worldPosition = (modelMatrix * vec4(displacedPosition, 1.0)).xyz;
  vec4 viewPosition = viewMatrix * vec4(worldPosition, 1.0);
  gl_Position = projectionMatrix * viewPosition;

  // Two neighboring samples along this vertex's tangent/bitangent directions (`tangent` comes
  // from geometry.computeTangents() in ReactiveSphere.tsx, enabled via the material's
  // USE_TANGENT define) - used only to numerically estimate the post-displacement normal below.
  float distanceA = (M_PI * 2.0) / uSubdivision.x;
  float distanceB = M_PI / uSubdivision.x;

  vec3 biTangent = cross(normal, tangent.xyz);

  vec3 positionA = position + tangent.xyz * distanceA;
  vec3 displacedPositionA = getDisplacedPosition(positionA);

  vec3 positionB = position + biTangent.xyz * distanceB;
  vec3 displacedPositionB = getDisplacedPosition(positionB);

  vec3 computedNormal = cross(displacedPositionA - displacedPosition, displacedPositionB - displacedPosition);
  computedNormal = normalize((modelMatrix * vec4(computedNormal, 0.0)).xyz);

  vNormal = computedNormal;
  vViewDirection = normalize(worldPosition - cameraPosition);
}
