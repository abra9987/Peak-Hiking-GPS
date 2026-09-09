import * as THREE from 'three';

const MAGIC = 0x494d4b50; // "PKMI" little-endian

/**
 * Loads a segment's real geometry: the triangles the game draws, with the
 * vertex colours its terrain shaders are driven by.
 *
 * This is what a heightfield cannot be. One altitude per XZ has no way to
 * express a cave, an overhang or a tunnel, and those are most of what makes a
 * climbing map worth looking at in three dimensions.
 *
 * See MeshExporter.cs for the layout.
 */
export async function loadSegmentMesh(baseUrl, mesh) {
  const url = `${baseUrl}/${mesh.file}`;
  const response = await fetch(url, { cache: 'no-cache' });

  if (!response.ok) {
    throw new Error(`Cannot load ${url}: ${response.status} ${response.statusText}`);
  }

  const buffer = await response.arrayBuffer();
  const header = new DataView(buffer);

  if (header.getUint32(0, true) !== MAGIC) {
    throw new Error(`${mesh.file} is not a PKMI mesh.`);
  }

  const version = header.getUint32(4, true);
  if (version !== 1) {
    throw new Error(`${mesh.file} is version ${version}; this build reads version 1.`);
  }

  const vertexCount = header.getUint32(8, true);
  const indexCount = header.getUint32(12, true);
  const hasColors = (header.getUint32(16, true) & 1) === 1;

  let offset = 20;

  // Copies rather than views: the source buffer's offsets are not guaranteed
  // to satisfy Float32Array's alignment requirement.
  const positions = new Float32Array(buffer.slice(offset, offset + vertexCount * 12));
  offset += vertexCount * 12;

  let colors = null;
  if (hasColors) {
    colors = new Uint8Array(buffer.slice(offset, offset + vertexCount * 4));
    offset += vertexCount * 4;
  }

  const indices = new Uint32Array(buffer.slice(offset, offset + indexCount * 4));

  return buildGeometry(positions, colors, indices, vertexCount);
}

function buildGeometry(positions, colors, indices, vertexCount) {
  // Unity is left-handed, Three.js is right-handed: negate Z, exactly as the
  // heightfield and markers do, so everything stays in one frame.
  for (let i = 2; i < positions.length; i += 3) {
    positions[i] = -positions[i];
  }

  // Negating one axis flips triangle winding, which would leave every face
  // pointing inward and the whole mountain lit from inside.
  for (let i = 0; i < indices.length; i += 3) {
    const swap = indices[i + 1];
    indices[i + 1] = indices[i + 2];
    indices[i + 2] = swap;
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setIndex(new THREE.BufferAttribute(indices, 1));

  if (colors) {
    // Drop alpha: the terrain shaders use it as a blend weight, not opacity,
    // and honouring it would render the ground see-through.
    const rgb = new Uint8Array(vertexCount * 3);
    for (let v = 0; v < vertexCount; v++) {
      rgb[v * 3] = colors[v * 4];
      rgb[v * 3 + 1] = colors[v * 4 + 1];
      rgb[v * 3 + 2] = colors[v * 4 + 2];
    }

    // Normalized byte attributes are read as linear, but these are
    // display-referred values from the game's materials.
    const linear = new Float32Array(vertexCount * 3);
    for (let i = 0; i < linear.length; i++) {
      const c = rgb[i] / 255;
      linear[i] = c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    }

    geometry.setAttribute('color', new THREE.BufferAttribute(linear, 3));
  }

  geometry.computeVertexNormals();
  geometry.computeBoundingSphere();

  const material = new THREE.MeshStandardMaterial({
    vertexColors: Boolean(colors),
    color: colors ? 0xffffff : 0x8a8f96,
    roughness: 0.92,
    metalness: 0.0,
    // Game meshes are authored for a camera that never sees their backs;
    // drawing both sides keeps cave interiors from disappearing.
    side: THREE.DoubleSide,
  });

  const object = new THREE.Mesh(geometry, material);
  object.name = 'segment-mesh';
  return object;
}
