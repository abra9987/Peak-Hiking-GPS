import { describe, kindLabel } from './taxonomy.js';

/**
 * The control panel: which biome, which layers, and what the snapshot is.
 *
 * Layer toggles are built from the markers actually present in the segment
 * rather than from a fixed list, so a biome with no wildlife does not show a
 * dead "Wildlife" switch, and a kind nobody anticipated still gets one.
 */
export function createUI({ onSelectSegment, onToggleKind }) {
  const segmentList = document.getElementById('segments');
  const layerList = document.getElementById('layers');
  const meta = document.getElementById('meta');

  const hiddenKinds = new Set();
  let buttons = [];

  function setSnapshot(snapshot) {
    segmentList.innerHTML = '';
    buttons = snapshot.segments.map((segment, index) => {
      const button = document.createElement('button');
      button.className = 'segment';
      button.textContent = segment.displayName || segment.biome;
      button.addEventListener('click', () => onSelectSegment(index));
      segmentList.appendChild(button);
      return button;
    });

    const captured = new Date(snapshot.generatedAt);
    meta.innerHTML = '';
    meta.appendChild(line('Captured', captured.toLocaleString()));
    meta.appendChild(line('Game', snapshot.gameVersion || 'unknown'));
    meta.appendChild(line('Next reset', formatCountdown()));

    // The daily map turns over at 17:00 UTC; a live countdown is more useful
    // than a timestamp nobody can convert in their head.
    setInterval(() => {
      const node = meta.querySelector('[data-field="Next reset"] .value');
      if (node) node.textContent = formatCountdown();
    }, 1000);
  }

  function setSegment(index, segment, byKind) {
    buttons.forEach((button, i) => button.classList.toggle('active', i === index));

    layerList.innerHTML = '';

    const counts = new Map();
    for (const marker of segment.markers) {
      counts.set(marker.kind, (counts.get(marker.kind) ?? 0) + 1);
    }

    const kinds = [...counts.keys()].sort((a, b) => counts.get(b) - counts.get(a));

    for (const kind of kinds) {
      const visible = !hiddenKinds.has(kind);
      const row = document.createElement('label');
      row.className = 'layer';

      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = visible;
      checkbox.addEventListener('change', () => {
        if (checkbox.checked) hiddenKinds.delete(kind);
        else hiddenKinds.add(kind);
        onToggleKind(kind, checkbox.checked);
      });

      const swatch = document.createElement('span');
      swatch.className = 'swatch';
      swatch.style.background = swatchColor(segment.markers, kind);

      const name = document.createElement('span');
      name.className = 'layer-name';
      name.textContent = kindLabel(kind);

      const count = document.createElement('span');
      count.className = 'layer-count';
      count.textContent = counts.get(kind);

      row.append(checkbox, swatch, name, count);
      layerList.appendChild(row);

      // Re-apply a toggle the user set on a previous biome.
      const group = byKind.get(kind);
      if (group) group.visible = visible;
    }
  }

  return { setSnapshot, setSegment };
}

function swatchColor(markers, kind) {
  const sample = markers.find((marker) => marker.kind === kind);
  return sample ? describe(sample).color : '#9aa4b0';
}

function line(label, value) {
  const div = document.createElement('div');
  div.className = 'meta-line';
  div.dataset.field = label;
  div.innerHTML = `<span class="label"></span><span class="value"></span>`;
  div.querySelector('.label').textContent = label;
  div.querySelector('.value').textContent = value;
  return div;
}

/** Time until the next 17:00 UTC daily reset. */
function formatCountdown() {
  const now = new Date();
  const next = new Date(now);
  next.setUTCHours(17, 0, 0, 0);
  if (next <= now) next.setUTCDate(next.getUTCDate() + 1);

  const diff = next - now;
  const hours = Math.floor(diff / 3600000);
  const minutes = Math.floor((diff % 3600000) / 60000);
  const seconds = Math.floor((diff % 60000) / 1000);
  const pad = (n) => String(n).padStart(2, '0');

  return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
}
