import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { applyMapTransformToCamera } from './overlayCamera'

/** A plausible map projection matrix: the sixteen values Google's transformer produces are opaque,
 * so the test only needs a matrix that is invertible and far from the identity. */
function mapMatrix(): number[] {
  const matrix = new THREE.Matrix4().makePerspective(-1, 1, 1, -1, 0.1, 10_000)
  matrix.elements[12] = 1234
  matrix.elements[13] = -5678
  return matrix.toArray()
}

describe('applyMapTransformToCamera (specs/087 FR-005)', () => {
  it('copies the map projection onto the camera and inverts it', () => {
    const camera = new THREE.PerspectiveCamera()
    const matrix = mapMatrix()

    applyMapTransformToCamera(camera, matrix)

    expect(camera.projectionMatrix.toArray()).toEqual(matrix)
    const product = new THREE.Matrix4()
      .multiplyMatrices(camera.projectionMatrixInverse, camera.projectionMatrix)
      .toArray()
    const identity = new THREE.Matrix4().toArray()
    product.forEach((value, index) => expect(value).toBeCloseTo(identity[index], 6))
  })

  it('resets a moved, rotated, scaled camera to the identity pose the map frame assumes', () => {
    const camera = new THREE.PerspectiveCamera()
    camera.position.set(50, -20, 90)
    camera.rotation.set(0.4, 1.1, -0.2)
    camera.scale.set(2, 2, 2)

    applyMapTransformToCamera(camera, mapMatrix())

    expect(camera.matrixWorld.toArray()).toEqual(new THREE.Matrix4().toArray())
    expect(camera.matrixWorldInverse.toArray()).toEqual(new THREE.Matrix4().toArray())
  })
})
