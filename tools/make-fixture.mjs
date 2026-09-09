/**
 * Generates a synthetic snapshot so the viewer can be developed and verified
 * without launching the game.
 *
 * This is not a convenience script — it is the contract test for
 * docs/DATA-FORMAT.md. If the viewer renders a fixture correctly, the format
 * is implementable from the document alone, which is the only way to know the
 * plugin and the web client agree about anything.
 *
 *   node tools/make-fixture.mjs [outDir]
 */

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(process.argv[2] ?? join(here, '..', 'web', 'public', 'data'));

const SEGMENTS = [
  { biome: 'Shore', displayName: 'Shore', base: 40, relief: 180, tint: [0.78, 0.74, 0.58] },
  { biome: 'Tropics', displayName: 'Tropics', base: 220, relief: 320, tint: [0.35, 0.55, 0.28] },
  { biome: 'Alpine', displayName: 'Alpine', base: 620, relief: 460, tint: [0.82, 0.85, 0.9] },
];

const HEIGHT_RES = 512;
const ALBEDO_RES = 512;
const SIZE = 1200; // world units per side

function main() {
  mkdirSync(outDir, { recursive: true });

  const snapshot = {
    schemaVersion: 1,
    generatedAt: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
    gameVersion: 'fixture',
    pluginVersion: '0.1.0',
    capture: { heightResolution: HEIGHT_RES, albedoResolution: ALBEDO_RES, raycastLayerMask: -5 },
    segments: [],
  };

  SEGMENTS.forEach((spec, index) => {
    const originX = -SIZE / 2;
    const originZ = index * SIZE * 1.05 - SIZE / 2;

    const { heights, min, max, coverage, mask } = generateHeights(spec, index);

    const heightFile = `segment_${index}.height.bin`;
    writeFileSync(join(outDir, heightFile), encodeHeightfield(heights, mask, min, max));

    const albedoFile = `segment_${index}.albedo.png`;
    writeFileSync(join(outDir, albedoFile), encodePng(renderAlbedo(spec, heights, mask, min, max)));

    snapshot.segments.push({
      index,
      biome: spec.biome,
      displayName: spec.displayName,
      bounds: { min: [originX, min, originZ], max: [originX + SIZE, max, originZ + SIZE] },
      terrain: {
        file: heightFile,
        width: HEIGHT_RES,
        depth: HEIGHT_RES,
        origin: [originX, originZ],
        size: [SIZE, SIZE],
        heightMin: min,
        heightMax: max,
        coverage,
      },
      albedo: {
        file: albedoFile,
        resolution: ALBEDO_RES,
        origin: [originX, originZ],
        size: [SIZE, SIZE],
      },
      markers: generateMarkers(spec, index, originX, originZ, heights, mask, min, max),
    });
  });

  writeFileSync(join(outDir, 'snapshot.json'), `${JSON.stringify(snapshot, null, 2)}\n`);
  console.log(`Fixture written to ${outDir} (${snapshot.segments.length} segments).`);
}

// ---------------------------------------------------------------------------

function generateHeights(spec, seedOffset) {
  const n = HEIGHT_RES;
  const heights = new Float32Array(n * n);
  const mask = new Uint8Array(n * n);
  let min = Infinity;
  let max = -Infinity;
  let hits = 0;

  for (let z = 0; z < n; z++) {
    for (let x = 0; x < n; x++) {
      const i = z * n + x;
      const u = x / (n - 1);
      const v = z / (n - 1);

      // A ridge running across the segment, plus octaves of value noise.
      const ridge = Math.exp(-((v - 0.5) ** 2) / 0.06);
      let h = spec.base + spec.relief * ridge;
      h += fbm(u * 6 + seedOffset * 11, v * 6, seedOffset) * spec.relief * 0.55;
      h += Math.sin(u * Math.PI * 3.1) * spec.relief * 0.08;

      // A hole, to prove no-data survives the round trip and renders as a gap
      // rather than as a floor at zero.
      const dx = u - 0.24;
      const dz = v - 0.72;
      if (dx * dx + dz * dz < 0.0035) continue;

      heights[i] = h;
      mask[i] = 1;
      hits++;
      if (h < min) min = h;
      if (h > max) max = h;
    }
  }

  return { heights, mask, min, max, coverage: hits / (n * n) };
}

function encodeHeightfield(heights, mask, min, max) {
  const span = max - min;
  const scale = span > 1e-6 ? 65534 / span : 0;
  const buffer = Buffer.alloc(heights.length * 2);

  for (let i = 0; i < heights.length; i++) {
    let value = 0;
    if (mask[i]) {
      value = Math.min(65534, Math.max(0, Math.round((heights[i] - min) * scale))) + 1;
    }
    buffer.writeUInt16LE(value, i * 2);
  }

  return buffer;
}

function renderAlbedo(spec, heights, mask, min, max) {
  const n = ALBEDO_RES;
  const pixels = Buffer.alloc(n * n * 3);
  const span = Math.max(1e-6, max - min);

  for (let y = 0; y < n; y++) {
    // PNG row 0 is the top of the image, which is maximum Z. The heightfield
    // stores row 0 at minimum Z, so the two are mirrored — exactly the
    // relationship docs/DATA-FORMAT.md promises the client.
    const z = n - 1 - y;

    for (let x = 0; x < n; x++) {
      const i = z * HEIGHT_RES + x;
      const o = (y * n + x) * 3;

      if (!mask[i]) continue; // holes stay black

      const t = (heights[i] - min) / span;
      const shade = 0.45 + 0.55 * t;

      pixels[o] = clamp255(spec.tint[0] * 255 * shade);
      pixels[o + 1] = clamp255(spec.tint[1] * 255 * shade);
      pixels[o + 2] = clamp255(spec.tint[2] * 255 * shade);
    }
  }

  return { width: n, height: n, pixels };
}

function generateMarkers(spec, index, originX, originZ, heights, mask, min, max) {
  const types = [
    ['luggage', 'LuggageSmall'],
    ['luggage', 'LuggageBig'],
    ['luggage', `Luggage${spec.biome}`],
    ['luggage', 'LuggageTrick'],
    ['luggage', 'LuggageAncient'],
    ['belltower', 'Belltower'],
    ['animal', 'Capybara'],
    ['animal', 'Beehive'],
    ['amulet', 'AMULET_DOUBLEJUMP'],
    ['statue', 'scout statue'],
  ];

  const markers = [];
  let seed = index * 977 + 13;

  for (let k = 0; k < 26; k++) {
    const [kind, type] = types[k % types.length];

    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
    const u = ((seed >>> 8) % 1000) / 1000;
    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
    const v = ((seed >>> 8) % 1000) / 1000;

    const gx = Math.min(HEIGHT_RES - 1, Math.floor(u * HEIGHT_RES));
    const gz = Math.min(HEIGHT_RES - 1, Math.floor(v * HEIGHT_RES));
    const i = gz * HEIGHT_RES + gx;
    if (!mask[i]) continue;

    markers.push({
      id: `${index}:${kind}:${k.toString(16).padStart(8, '0')}`,
      kind,
      type,
      name: null,
      // +2 so the icon sits above the surface rather than inside it.
      pos: [originX + u * SIZE, heights[i] + 2, originZ + v * SIZE],
    });
  }

  return markers;
}

// --- tiny PNG encoder (RGB8, no filtering) ---------------------------------

function encodePng({ width, height, pixels }) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    const src = y * width * 3;
    const dst = y * (width * 3 + 1);
    raw[dst] = 0; // filter: none
    pixels.copy(raw, dst + 1, src, src + width * 3);
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;  // bit depth
  ihdr[9] = 2;  // colour type: truecolour
  ihdr[10] = 0; // deflate
  ihdr[11] = 0; // adaptive filtering
  ihdr[12] = 0; // no interlace

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 6 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);

  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);

  return Buffer.concat([length, body, crc]);
}

// Built on first use: the top-level code above runs before a `const` in this
// part of the file would be initialised.
let crcTable = null;

function crc32(buffer) {
  if (crcTable === null) {
    crcTable = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crcTable[n] = c >>> 0;
    }
  }

  let c = 0xffffffff;
  for (let i = 0; i < buffer.length; i++) c = crcTable[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

// --- noise -----------------------------------------------------------------

function fbm(x, y, seed) {
  let value = 0;
  let amplitude = 0.5;
  let frequency = 1;

  for (let octave = 0; octave < 5; octave++) {
    value += amplitude * valueNoise(x * frequency, y * frequency, seed + octave);
    amplitude *= 0.5;
    frequency *= 2;
  }

  return value;
}

function valueNoise(x, y, seed) {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const xf = x - xi;
  const yf = y - yi;

  const smooth = (t) => t * t * (3 - 2 * t);
  const sx = smooth(xf);
  const sy = smooth(yf);

  const n00 = hash(xi, yi, seed);
  const n10 = hash(xi + 1, yi, seed);
  const n01 = hash(xi, yi + 1, seed);
  const n11 = hash(xi + 1, yi + 1, seed);

  return (n00 * (1 - sx) + n10 * sx) * (1 - sy) + (n01 * (1 - sx) + n11 * sx) * sy;
}

function hash(x, y, seed) {
  let h = x * 374761393 + y * 668265263 + seed * 1274126177;
  h = (h ^ (h >>> 13)) * 1274126177;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
}

function clamp255(v) {
  return Math.max(0, Math.min(255, Math.round(v)));
}

// Entry point last, so every helper above is initialised before it runs.
main();
