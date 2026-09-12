import * as THREE from 'three'
import { GLTFLoader, type GLTF } from 'three/examples/jsm/loaders/GLTFLoader.js'
import { apiFetch, API_BASE_URL } from '../../../api/httpClient'
import { buildElementIndex, type ElementIndex, type IndexableNode } from '../../elements/elementIndex'
import type { ContentFailureReason } from '../ViewerContent'

export interface GltfLoadResult {
  root: THREE.Object3D
  elementIndex: ElementIndex
}

export type GltfLoadOutcome =
  | { ok: true; result: GltfLoadResult }
  | { ok: false; reason: ContentFailureReason }

function resolveSignedUrl(url: string): string {
  return `${API_BASE_URL.replace(/\/api\/v1$/, '')}${url}`
}

/** spec Assumptions — content is served through the platform's existing signed-URL file access
 * mechanism (the same one `features/documents/api/documentsApi.ts` already uses for downloads);
 * neither Lucy nor content may cause the viewer to fetch an arbitrary external address. `fileId`
 * is never treated as a URL itself. */
async function resolveContentUrl(fileId: string): Promise<string> {
  const { url } = await apiFetch<{ url: string }>(`/documents/${fileId}/download`)
  return resolveSignedUrl(url)
}

function collectIndexableNodes(root: THREE.Object3D): IndexableNode[] {
  const nodes: IndexableNode[] = []
  root.traverse((node) => {
    const extras = (node.userData?.gltfExtras ?? node.userData?.extras) as Record<string, unknown> | undefined
    if (extras && Object.keys(extras).length > 0) {
      nodes.push({ elementId: node.uuid, properties: extras })
    }
  })
  return nodes
}

/** T027 (US1) — loads a glTF via `GLTFLoader`, reads each node's `extras`/`userData` into the
 * shared `elementIndex.ts` (built in Foundational, T023a — see that module's own doc comment for
 * why this is not a US1→US3 inverted dependency). Reports `unsupported-format` for anything that
 * isn't `.gltf`/`.glb`, and `unreachable-or-corrupt` for a resolvable-but-failing load — never
 * throws (FR-007, FR-035). */
export async function loadGltfContent(fileId: string): Promise<GltfLoadOutcome> {
  let url: string
  try {
    url = await resolveContentUrl(fileId)
  } catch {
    return { ok: false, reason: 'unreachable-or-corrupt' }
  }

  if (!/\.(gltf|glb)(\?|$)/i.test(url)) {
    return { ok: false, reason: 'unsupported-format' }
  }

  const loader = new GLTFLoader()
  let gltf: GLTF
  try {
    gltf = await loader.loadAsync(url)
  } catch {
    return { ok: false, reason: 'unreachable-or-corrupt' }
  }

  const elementIndex = buildElementIndex(collectIndexableNodes(gltf.scene))
  return { ok: true, result: { root: gltf.scene, elementIndex } }
}
