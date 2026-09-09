/**
 * How raw game types are presented.
 *
 * The capture exports whatever the game calls a thing, verbatim. Deciding that
 * two of those are "the same chest with a different loot table" is a judgement
 * about presentation, so it lives here and not in the plugin — the underlying
 * type is always still there to filter or research against.
 *
 * Icons are drawn, never imported. The reference project ships PNGs lifted
 * straight out of the game, which makes its repository undistributable no
 * matter what licence it claims. Nothing here is anyone else's artwork.
 */

/**
 * Biome-flavoured luggage. Visually one chest; the class differs because the
 * loot table does — which items a chest can roll, weighted differently as you
 * climb. Grouped under one icon, with the biome flavour kept as a detail.
 */
const BIOME_LUGGAGE = new Set([
  'LuggageBeach',
  'LuggageTropics',
  'LuggageJungle',
  'LuggageRoots',
  'LuggageAlpine',
  'LuggageTundra',
  'LuggageMesa',
  'LuggageCaldera',
  'LuggageGloom',
  'LuggageCitadel',
  'LuggageClimber',
]);

/**
 * Luggage that behaves differently, not merely rolls differently.
 * These stay visually distinct because mistaking one for a plain chest costs
 * the player something.
 */
const SPECIAL_LUGGAGE = {
  LuggageTrick: { label: 'Mimic', color: '#e0443e', glyph: '!', danger: true },
  LuggageCursed: { label: 'Cursed luggage', color: '#8b46c8', glyph: '?' },
  LuggageAncient: { label: 'Ancient luggage', color: '#c9a227', glyph: 'A' },
  LuggageEpic: { label: "Explorer's luggage", color: '#ff6a1a', glyph: 'E' },
  LuggageBig: { label: 'Big luggage', color: '#ece2dc', glyph: 'B' },
  LuggageSmall: { label: 'Luggage', color: '#da7c20', glyph: 'L' },
  LuggageClown: { label: 'Clown luggage', color: '#ff4fa3', glyph: 'C' },
};

const KIND_DEFAULTS = {
  luggage: { label: 'Luggage', color: '#da7c20', glyph: 'L' },
  belltower: { label: 'Belltower', color: '#a057d8', glyph: 'T' },
  animal: { label: 'Wildlife', color: '#8d6a45', glyph: 'W' },
  amulet: { label: 'Amulet', color: '#3ddc84', glyph: 'M' },
  tomb: { label: 'Tomb', color: '#7f8c9b', glyph: 'X' },
  statue: { label: 'Statue', color: '#64636d', glyph: 'S' },
  campfire: { label: 'Campfire', color: '#ff8c42', glyph: 'F' },
  misc: { label: 'Other', color: '#9aa4b0', glyph: '?' },
};

const ANIMALS = {
  Capybara: { label: 'Capybara', color: '#a0703f', glyph: 'C' },
  Beehive: { label: 'Beehive', color: '#e8c33a', glyph: 'H' },
  EarlyWorm: { label: 'Early worm', color: '#f19ab5', glyph: 'W' },
  Antlion: { label: 'Antlion', color: '#c46a2a', glyph: 'A' },
};

const AMULETS = {
  AMULET_DOUBLEJUMP: { label: "Scout's Initiative", color: '#5ad1ff', glyph: 'J' },
  AMULET_CLONE: { label: 'Clone amulet', color: '#adff2f', glyph: 'K' },
  AMULET_HEALING: { label: 'Healing amulet', color: '#54e08a', glyph: 'H' },
  AMULET_INFINITESTAM: { label: 'Endless stamina', color: '#ffd93d', glyph: 'S' },
};

/**
 * Resolves a marker to how it should look and read.
 * Unknown types are never dropped — they fall back to their kind, and failing
 * that to a neutral marker, so new content appears the day it ships instead of
 * silently going missing.
 */
export function describe(marker) {
  const { kind, type } = marker;

  if (SPECIAL_LUGGAGE[type]) {
    return { ...SPECIAL_LUGGAGE[type], kind, type, variant: null };
  }

  if (BIOME_LUGGAGE.has(type)) {
    return {
      ...KIND_DEFAULTS.luggage,
      kind,
      type,
      // e.g. LuggageCaldera -> "Caldera", shown as a loot-table hint.
      variant: type.replace(/^Luggage/, ''),
    };
  }

  if (ANIMALS[type]) return { ...ANIMALS[type], kind, type, variant: null };
  if (AMULETS[type]) return { ...AMULETS[type], kind, type, variant: null };

  const fallback = KIND_DEFAULTS[kind] ?? KIND_DEFAULTS.misc;
  return { ...fallback, kind, type, variant: null, label: fallback.label };
}

export function kindLabel(kind) {
  return (KIND_DEFAULTS[kind] ?? KIND_DEFAULTS.misc).label;
}

export const KINDS = Object.keys(KIND_DEFAULTS);
