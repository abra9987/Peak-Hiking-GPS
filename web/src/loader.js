/**
 * Snapshot loading and the one place Unity coordinates become Three.js ones.
 *
 * Unity is left-handed (+Z forward), Three.js is right-handed (+Z toward the
 * viewer). Converting in a single function, applied to terrain vertices and
 * markers alike, is what keeps the two provably consistent — a marker cannot
 * drift away from the ground it sits on if both went through the same
 * transform.
 */

/** Unity world space -> Three.js world space. */
export function toThree(x, y, z) {
  return [x, y, -z];
}

/** Value stored in a heightfield meaning "no geometry under this sample". */
export const NO_DATA = 0;

export async function loadSnapshot(baseUrl) {
  const manifestUrl = `${baseUrl}/snapshot.json`;
  const response = await fetch(manifestUrl, { cache: 'no-cache' });

  if (!response.ok) {
    throw new Error(`Cannot load ${manifestUrl}: ${response.status} ${response.statusText}`);
  }

  const snapshot = await response.json();

  if (snapshot.schemaVersion !== 1) {
    throw new Error(
      `Snapshot schema v${snapshot.schemaVersion} is not supported by this build (expected v1).`,
    );
  }

  return snapshot;
}

/**
 * Fetches a heightfield and decodes it into world-space heights.
 *
 * Returns a Float32Array of heights plus a Uint8Array mask, rather than the
 * raw uint16 values, so nothing downstream has to remember the encoding.
 */
export async function loadHeightfield(baseUrl, terrain) {
  const url = `${baseUrl}/${terrain.file}`;
  const response = await fetch(url, { cache: 'no-cache' });

  if (!response.ok) {
    throw new Error(`Cannot load ${url}: ${response.status} ${response.statusText}`);
  }

  const buffer = await maybeDecompress(response, terrain.file);
  const raw = new Uint16Array(buffer);
  const expected = terrain.width * terrain.depth;

  if (raw.length !== expected) {
    throw new Error(
      `${terrain.file} holds ${raw.length} samples, manifest declares ${expected}.`,
    );
  }

  const heights = new Float32Array(expected);
  const mask = new Uint8Array(expected);
  const span = terrain.heightMax - terrain.heightMin;

  for (let i = 0; i < expected; i++) {
    const v = raw[i];
    if (v === NO_DATA) continue;

    mask[i] = 1;
    heights[i] = terrain.heightMin + ((v - 1) / 65534) * span;
  }

  const colors = await loadGroundColors(baseUrl, terrain, expected);
  return { heights, mask, colors };
}

/**
 * Ground colours, one RGB triple per height sample.
 *
 * This is the surface the player actually walks on, taken from the material
 * each sampling ray landed on: sand on the shore, snow higher up. Absent in
 * older snapshots, in which case the caller falls back to shading by altitude.
 */
async function loadGroundColors(baseUrl, terrain, expected) {
  if (!terrain.colorFile) return null;

  const response = await fetch(`${baseUrl}/${terrain.colorFile}`, { cache: 'no-cache' });
  if (!response.ok) return null;

  const bytes = new Uint8Array(await response.arrayBuffer());
  return bytes.length === expected * 3 ? bytes : null;
}

/**
 * Snapshots are published gzipped to keep transfer small. A server that
 * already decompressed for us hands back a plain body, so sniff rather than
 * assume.
 */
async function maybeDecompress(response, filename) {
  const buffer = await response.arrayBuffer();
  const looksGzipped = filename.endsWith('.gz') && isGzip(buffer);

  if (!looksGzipped) return buffer;

  const stream = new Blob([buffer]).stream().pipeThrough(new DecompressionStream('gzip'));
  return new Response(stream).arrayBuffer();
}

function isGzip(buffer) {
  if (buffer.byteLength < 2) return false;
  const head = new Uint8Array(buffer, 0, 2);
  return head[0] === 0x1f && head[1] === 0x8b;
}
