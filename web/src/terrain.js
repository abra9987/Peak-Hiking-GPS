import * as THREE from 'three';

/**
 * Turns a decoded heightfield into a mesh.
 *
 * Samples with no data are dropped rather than flattened to zero: a segment
 * captured with holes shows holes, instead of inventing a floor that does not
 * exist in the game.
 */
export function buildTerrainMesh(terrain, heights, mask, texture) {
  const { width, depth, origin, size } = terrain;
  const stepX = size[0] / width;
  const stepZ = size[1] / depth;

  // Vertex slots are allocated for the full grid so that index arithmetic
  // stays trivial; unused slots are simply never referenced by a triangle.
  const positions = new Float32Array(width * depth * 3);
  const uvs = new Float32Array(width * depth * 2);

  for (let z = 0; z < depth; z++) {
    for (let x = 0; x < width; x++) {
      const i = z * width + x;
      const worldX = origin[0] + (x + 0.5) * stepX;
      const worldZ = origin[1] + (z + 0.5) * stepZ;

      positions[i * 3] = worldX;
      positions[i * 3 + 1] = heights[i];
      positions[i * 3 + 2] = -worldZ; // Unity -> Three.js handedness

      uvs[i * 2] = (x + 0.5) / width;
      uvs[i * 2 + 1] = (z + 0.5) / depth;
    }
  }

  const indices = buildIndices(width, depth, mask);

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();
  geometry.computeBoundingSphere();

  const material = new THREE.MeshStandardMaterial({
    map: texture ?? null,
    roughness: 0.95,
    metalness: 0.0,
    flatShading: false,
  });

  const mesh = new THREE.Mesh(geometry, material);
  mesh.name = 'terrain';
  mesh.receiveShadow = false;
  mesh.castShadow = false;

  return mesh;
}

/**
 * Emits two triangles per grid cell, but only where all four corners have
 * data. Winding is chosen so face normals point up (+Y) — get it backwards and
 * computeVertexNormals lights the whole mountain from underneath.
 */
function buildIndices(width, depth, mask) {
  const cells = (width - 1) * (depth - 1);
  const indices = new Uint32Array(cells * 6);
  let n = 0;

  for (let z = 0; z < depth - 1; z++) {
    for (let x = 0; x < width - 1; x++) {
      const a = z * width + x;
      const b = a + 1;
      const c = a + width;
      const d = c + 1;

      if (!mask[a] || !mask[b] || !mask[c] || !mask[d]) continue;

      indices[n++] = a; indices[n++] = b; indices[n++] = c;
      indices[n++] = b; indices[n++] = d; indices[n++] = c;
    }
  }

  return new THREE.BufferAttribute(indices.subarray(0, n), 1);
}

/**
 * Samples the heightfield at an arbitrary world XZ, bilinearly.
 * Used for placing things on the surface and for the cursor readout.
 * Returns null outside the captured area or over a hole.
 */
export function sampleHeight(terrain, heights, mask, worldX, worldZ) {
  const { width, depth, origin, size } = terrain;

  const fx = ((worldX - origin[0]) / size[0]) * width - 0.5;
  const fz = ((worldZ - origin[1]) / size[1]) * depth - 0.5;

  const x0 = Math.floor(fx);
  const z0 = Math.floor(fz);
  if (x0 < 0 || z0 < 0 || x0 + 1 >= width || z0 + 1 >= depth) return null;

  const i00 = z0 * width + x0;
  const i10 = i00 + 1;
  const i01 = i00 + width;
  const i11 = i01 + 1;

  if (!mask[i00] || !mask[i10] || !mask[i01] || !mask[i11]) return null;

  const tx = fx - x0;
  const tz = fz - z0;

  const top = heights[i00] * (1 - tx) + heights[i10] * tx;
  const bottom = heights[i01] * (1 - tx) + heights[i11] * tx;

  return top * (1 - tz) + bottom * tz;
}
