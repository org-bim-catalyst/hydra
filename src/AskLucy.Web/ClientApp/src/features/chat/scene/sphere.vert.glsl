// Displaces the sphere's *surface* using 3D simplex noise, driven by uTime (idle drift) and
// uAmplitude/uFrequency (idle vs. voice-reactive — ReactiveSphere.tsx animates these; see
// research.md §1/§2/§3). Pivoted 2026-09-06 (live user review) from a particle-cloud
// (THREE.Points) technique to a continuous displaced mesh, after that technique proved
// fragile across GPUs in production: a NaN from this same noise function discarded every
// point on one machine, and — once that was patched — additive-blend overlap of thousands of
// points saturated into a solid blob on another. A single opaque, normally-shaded surface has
// neither failure mode: there's nothing to discard or oversaturate.
//
// Because a single scalar displacement function has no analytic derivative to hand-differentiate
// for a normal, each vertex's normal is instead estimated numerically: sample the same
// displacement function at two nearby points on the sphere's local tangent plane, and take the
// cross product of the resulting displaced-surface offsets (Bruno Simon's "organic sphere"
// technique — see organic-sphere.vercel.app, the reference this pivot drew from).

uniform float uTime;
uniform float uAmplitude;
uniform float uFrequency;
// spec 011-particle-sphere-engine FR-006, research.md §4 — a slow idle "breathing" pulse,
// computed once per frame on the CPU (sphereBreath.ts) and added here alongside the noise/
// reactive displacement so breathing, idle wobble, and voice-reactive deformation all layer
// additively rather than needing separate branching logic.
uniform float uBreath;

varying float vDisplacement;
varying vec3 vViewNormal;
varying vec3 vViewDir;

// Ashima Arts 3D simplex noise (webgl-noise, MIT license) — the standard inline GLSL
// noise function; no CPU/JS noise library can run inside a vertex shader.
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x * 34.0) + 1.0) * x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(vec3 v) {
  const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
  const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);

  vec3 i = floor(v + dot(v, C.yyy));
  vec3 x0 = v - i + dot(i, C.xxx);

  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min(g.xyz, l.zxy);
  vec3 i2 = max(g.xyz, l.zxy);

  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy;
  vec3 x3 = x0 - D.yyy;

  i = mod289(i);
  vec4 p = permute(permute(permute(
    i.z + vec4(0.0, i1.z, i2.z, 1.0))
    + i.y + vec4(0.0, i1.y, i2.y, 1.0))
    + i.x + vec4(0.0, i1.x, i2.x, 1.0));

  float n_ = 0.142857142857;
  vec3 ns = n_ * D.wyz - D.xzx;

  vec4 j = p - 49.0 * floor(p * ns.z * ns.z);

  vec4 x_ = floor(j * ns.z);
  vec4 y_ = floor(j - 7.0 * x_);

  vec4 x = x_ * ns.x + ns.yyyy;
  vec4 y = y_ * ns.x + ns.yyyy;
  vec4 h = 1.0 - abs(x) - abs(y);

  vec4 b0 = vec4(x.xy, y.xy);
  vec4 b1 = vec4(x.zw, y.zw);

  vec4 s0 = floor(b0) * 2.0 + 1.0;
  vec4 s1 = floor(b1) * 2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));

  vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
  vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

  vec3 p0 = vec3(a0.xy, h.x);
  vec3 p1 = vec3(a0.zw, h.y);
  vec3 p2 = vec3(a1.xy, h.z);
  vec3 p3 = vec3(a1.zw, h.w);

  vec4 norm = taylorInvSqrt(vec4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
  p0 *= norm.x;
  p1 *= norm.y;
  p2 *= norm.z;
  p3 *= norm.w;

  vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
  m = m * m;
  return 42.0 * dot(m * m, vec4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// Diagnosed on an NVIDIA RTX 3080 (ANGLE D3D11 backend): the noise function above evaluated to
// NaN on that GPU/driver even though the identical GLSL runs correctly elsewhere. Guarded here
// (rather than inside snoise() itself, which is third-party) so a single bad sample can't send
// gl_Position non-finite for either the "here" sample or either of the two neighbor samples
// used for normal estimation below.
float displacementAt(vec3 p) {
  float n = snoise(p * uFrequency + vec3(0.0, 0.0, uTime * 0.15)) * uAmplitude + uBreath;
  return isnan(n) ? 0.0 : n;
}

void main() {
  vec3 normalDir = normalize(position);

  // A local tangent-plane basis at this vertex, used only to sample two nearby points for the
  // numerical normal estimate below — not a true per-vertex tangent attribute (the geometry
  // doesn't need UVs for this). Falls back to a different "up" reference near the poles, where
  // cross(normalDir, vec3(0,1,0)) would otherwise degenerate toward zero length.
  vec3 referenceUp = abs(normalDir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
  vec3 tangent = normalize(cross(normalDir, referenceUp));
  vec3 bitangent = normalize(cross(normalDir, tangent));

  float eps = 0.01;
  vec3 neighborTangentPos = position + tangent * eps;
  vec3 neighborBitangentPos = position + bitangent * eps;

  float dHere = displacementAt(position);
  float dTangent = displacementAt(neighborTangentPos);
  float dBitangent = displacementAt(neighborBitangentPos);

  vec3 displacedHere = position + normalDir * dHere;
  vec3 displacedTangent = neighborTangentPos + normalize(neighborTangentPos) * dTangent;
  vec3 displacedBitangent = neighborBitangentPos + normalize(neighborBitangentPos) * dBitangent;

  // cross(T, B) recovers the outward normal direction for an orthonormal (T, B, N) basis where
  // B = cross(N, T) — same relationship applied here to the *displaced* offsets, so the result
  // tilts to reflect the noise surface's actual slope instead of just the base sphere's normal.
  vec3 computedNormal = normalize(cross(displacedTangent - displacedHere, displacedBitangent - displacedHere));

  vDisplacement = dHere;

  vec4 mvPosition = modelViewMatrix * vec4(displacedHere, 1.0);
  gl_Position = projectionMatrix * mvPosition;

  vViewNormal = normalize(normalMatrix * computedNormal);
  vViewDir = normalize(-mvPosition.xyz);
}
