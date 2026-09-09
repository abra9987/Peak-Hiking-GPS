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

  // A downward ray stops at whatever is on top, so a tree canopy or a barrier
  // becomes a sample tens of metres above the ground beside it. Joining those
  // to their neighbours extrudes vertical curtains across the map. Cutting the
  // mesh at implausible slopes leaves the ground surface and drops the props.
  const maxStep = Math.max(stepX, stepZ) * 10;
  const indices = buildIndices(width, depth, mask, heights, maxStep);

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
  geometry.setIndex(indices);

  // No orthophoto: colour by altitude instead. Combined with the scene lights
  // shading the real surface normals, this reads as a topographic map — which
  // suits a climbing game better than a photograph would, since height is the
  // thing the player is actually reasoning about.
  if (!texture) {
    geometry.setAttribute(
      'color',
      new THREE.BufferAttribute(elevationColours(heights, mask, terrain), 3),
    );
  }

  geometry.computeVertexNormals();
  geometry.computeBoundingSphere();

  const material = new THREE.MeshStandardMaterial({
    map: texture ?? null,
    vertexColors: !texture,
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
 * Altitude ramp: wet sand at sea level through vegetation and rock to snow.
 *
 * Stops are placed on the segment's own range rather than an absolute scale,
 * so a 300 m shore and a 1300 m summit both use the full ramp and stay
 * readable. Absolute altitude is still available on every marker.
 */
const RAMP = [
  [0.0, [0.42, 0.40, 0.31]],
  [0.12, [0.36, 0.42, 0.25]],
  [0.35, [0.31, 0.38, 0.22]],
  [0.58, [0.44, 0.40, 0.32]],
  [0.76, [0.52, 0.50, 0.48]],
  [0.9, [0.78, 0.79, 0.82]],
  [1.0, [0.95, 0.96, 0.98]],
];

function elevationColours(heights, mask, terrain) {
  const colours = new Float32Array(heights.length * 3);
  const span = Math.max(1e-6, terrain.heightMax - terrain.heightMin);

  for (let i = 0; i < heights.length; i++) {
    if (!mask[i]) continue;

    const t = Math.min(1, Math.max(0, (heights[i] - terrain.heightMin) / span));

    let lo = RAMP[0];
    let hi = RAMP[RAMP.length - 1];
    for (let s = 0; s < RAMP.length - 1; s++) {
      if (t >= RAMP[s][0] && t <= RAMP[s + 1][0]) {
        lo = RAMP[s];
        hi = RAMP[s + 1];
        break;
      }
    }

    const k = hi[0] === lo[0] ? 0 : (t - lo[0]) / (hi[0] - lo[0]);
    colours[i * 3] = lo[1][0] + (hi[1][0] - lo[1][0]) * k;
    colours[i * 3 + 1] = lo[1][1] + (hi[1][1] - lo[1][1]) * k;
    colours[i * 3 + 2] = lo[1][2] + (hi[1][2] - lo[1][2]) * k;
  }

  return colours;
}

/**
 * Emits two triangles per grid cell, but only where all four corners have
 * data. Winding is chosen so face normals point up (+Y) — get it backwards and
 * computeVertexNormals lights the whole mountain from underneath.
 */
function buildIndices(width, depth, mask, heights, maxStep) {
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

      const ha = heights[a];
      const hb = heights[b];
      const hc = heights[c];
      const hd = heights[d];

      const lo = Math.min(ha, hb, hc, hd);
      const hi = Math.max(ha, hb, hc, hd);
      if (hi - lo > maxStep) continue; // a cliff this steep is a prop, not ground

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
