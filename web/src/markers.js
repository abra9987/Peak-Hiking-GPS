import * as THREE from 'three';
import { describe } from './taxonomy.js';

const iconCache = new Map();

/**
 * Draws a marker icon on a canvas.
 *
 * Generating icons instead of shipping images keeps the repository free of
 * game artwork, and means a type nobody has drawn an icon for still renders
 * as something sensible rather than a broken image.
 */
function iconTexture(color, glyph, danger) {
  const key = `${color}|${glyph}|${danger ? 1 : 0}`;
  const cached = iconCache.get(key);
  if (cached) return cached;

  const size = 128;
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;

  const ctx = canvas.getContext('2d');
  const center = size / 2;
  const radius = size * 0.36;

  // Dark halo, so a light marker stays legible against snow and a dark one
  // against rock. Without it markers vanish over parts of nearly every biome.
  ctx.beginPath();
  ctx.arc(center, center, radius + 7, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(0, 0, 0, 0.55)';
  ctx.fill();

  ctx.beginPath();
  ctx.arc(center, center, radius, 0, Math.PI * 2);
  ctx.fillStyle = color;
  ctx.fill();

  ctx.lineWidth = danger ? 9 : 6;
  ctx.strokeStyle = danger ? '#ffffff' : 'rgba(255, 255, 255, 0.85)';
  ctx.stroke();

  ctx.fillStyle = pickInk(color);
  ctx.font = `700 ${Math.round(size * 0.42)}px ui-sans-serif, system-ui, sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(glyph, center, center + 2);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 4;
  iconCache.set(key, texture);

  return texture;
}

/** Picks black or white text for contrast against the disc colour. */
function pickInk(hex) {
  const c = hex.replace('#', '');
  const r = parseInt(c.slice(0, 2), 16);
  const g = parseInt(c.slice(2, 4), 16);
  const b = parseInt(c.slice(4, 6), 16);
  // Rec. 601 luma is close enough for a two-way choice.
  const luma = (r * 299 + g * 587 + b * 114) / 1000;
  return luma > 150 ? '#101216' : '#ffffff';
}

/**
 * Builds one sprite per marker, grouped so layers can be toggled by kind.
 * Sprites carry their marker in userData, which is what the hover raycast
 * reads back.
 */
export function buildMarkers(markers, { scale = 14 } = {}) {
  const group = new THREE.Group();
  group.name = 'markers';

  const byKind = new Map();

  for (const marker of markers) {
    const look = describe(marker);
    const material = new THREE.SpriteMaterial({
      map: iconTexture(look.color, look.glyph, look.danger),
      depthTest: true,
      depthWrite: false,
      transparent: true,
      sizeAttenuation: false,
    });

    const sprite = new THREE.Sprite(material);
    const [x, y, z] = marker.pos;
    sprite.position.set(x, y, -z); // Unity -> Three.js, same rule as terrain
    sprite.scale.setScalar(scale / 1000);
    sprite.renderOrder = look.danger ? 2 : 1;
    sprite.userData.marker = marker;
    sprite.userData.look = look;

    if (!byKind.has(marker.kind)) {
      const kindGroup = new THREE.Group();
      kindGroup.name = `markers:${marker.kind}`;
      byKind.set(marker.kind, kindGroup);
      group.add(kindGroup);
    }

    byKind.get(marker.kind).add(sprite);
  }

  return { group, byKind };
}

/**
 * Screen-space size is deliberate: markers stay the same size on screen no
 * matter how far the camera is, so a distant belltower is still clickable.
 * That is what sizeAttenuation: false above buys, and why scale is divided by
 * a constant rather than tied to world units.
 */
export function setKindVisible(byKind, kind, visible) {
  const group = byKind.get(kind);
  if (group) group.visible = visible;
}

export function setTypeVisible(byKind, predicate) {
  for (const group of byKind.values()) {
    for (const sprite of group.children) {
      sprite.visible = predicate(sprite.userData.marker);
    }
  }
}
