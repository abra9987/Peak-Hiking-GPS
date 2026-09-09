import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

import { loadSnapshot, loadHeightfield } from './loader.js';
import { buildTerrainMesh } from './terrain.js';
import { buildMarkers } from './markers.js';
import { describe } from './taxonomy.js';
import { createUI } from './ui.js';

const DATA_URL = import.meta.env.VITE_DATA_URL ?? './data';

const app = document.getElementById('app');
const tooltip = document.getElementById('tooltip');
const status = document.getElementById('status');

const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.outputColorSpace = THREE.SRGBColorSpace;
app.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0d1117);
scene.fog = new THREE.Fog(0x0d1117, 2000, 6000);

const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 1, 20000);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.maxPolarAngle = Math.PI * 0.495; // never let the camera go under the terrain
controls.screenSpacePanning = false;

// A hemisphere light keeps shaded slopes readable; a single directional light
// gives the relief enough shading to be read as relief at all.
scene.add(new THREE.HemisphereLight(0xbfd4ff, 0x30281f, 1.5));
const sun = new THREE.DirectionalLight(0xfff2e0, 2.0);
sun.position.set(-0.6, 1.0, 0.45).normalize();
scene.add(sun);

const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
let hovered = null;

const state = {
  snapshot: null,
  segmentIndex: -1,
  terrainMesh: null,
  markerGroup: null,
  markersByKind: null,
  textureLoader: new THREE.TextureLoader(),
};

const ui = createUI({
  onSelectSegment: (index) => loadSegment(index),
  onToggleKind: (kind, visible) => {
    const group = state.markersByKind?.get(kind);
    if (group) group.visible = visible;
  },
});

init().catch(reportFatal);

async function init() {
  setStatus('Loading snapshot…');
  state.snapshot = await loadSnapshot(DATA_URL);

  ui.setSnapshot(state.snapshot);
  await loadSegment(0);

  window.addEventListener('resize', onResize);
  renderer.domElement.addEventListener('pointermove', onPointerMove);
  renderer.domElement.addEventListener('pointerleave', () => setHovered(null));

  renderer.setAnimationLoop(render);
}

async function loadSegment(index) {
  const segment = state.snapshot.segments[index];
  if (!segment) return;

  setStatus(`Loading ${segment.displayName}…`);
  disposeSegment();
  state.segmentIndex = index;

  // albedo is optional: a segment whose orthophoto could not be captured is
  // shaded from its own heightfield instead.
  const [{ heights, mask }, texture] = await Promise.all([
    loadHeightfield(DATA_URL, segment.terrain),
    segment.albedo ? loadTexture(`${DATA_URL}/${segment.albedo.file}`) : Promise.resolve(null),
  ]);

  state.terrainMesh = buildTerrainMesh(segment.terrain, heights, mask, texture);
  scene.add(state.terrainMesh);

  const { group, byKind } = buildMarkers(segment.markers);
  state.markerGroup = group;
  state.markersByKind = byKind;
  scene.add(group);

  frameCamera(segment);
  ui.setSegment(index, segment, byKind);
  setStatus('');
}

function loadTexture(url) {
  return new Promise((resolve) => {
    state.textureLoader.load(
      url,
      (texture) => {
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
        // The orthophoto covers exactly the captured frame; sampling beyond it
        // should stretch the edge, never tile a second copy of the mountain in.
        texture.wrapS = THREE.ClampToEdgeWrapping;
        texture.wrapT = THREE.ClampToEdgeWrapping;
        resolve(texture);
      },
      undefined,
      () => resolve(null), // a missing orthophoto costs colour, not the mesh
    );
  });
}

function frameCamera(segment) {
  const { origin, size } = segment.terrain;
  const centerX = origin[0] + size[0] / 2;
  const centerZ = -(origin[1] + size[1] / 2);
  const centerY = (segment.terrain.heightMin + segment.terrain.heightMax) / 2;

  controls.target.set(centerX, centerY, centerZ);

  const span = Math.max(size[0], size[1]);
  camera.position.set(centerX + span * 0.55, segment.terrain.heightMax + span * 0.45, centerZ + span * 0.75);
  camera.near = Math.max(1, span / 5000);
  camera.far = span * 12;
  camera.updateProjectionMatrix();

  controls.minDistance = span * 0.03;
  controls.maxDistance = span * 3;
  controls.update();
}

function disposeSegment() {
  if (state.terrainMesh) {
    scene.remove(state.terrainMesh);
    state.terrainMesh.geometry.dispose();
    state.terrainMesh.material.map?.dispose();
    state.terrainMesh.material.dispose();
    state.terrainMesh = null;
  }

  if (state.markerGroup) {
    scene.remove(state.markerGroup);
    state.markerGroup.traverse((object) => {
      // Icon textures are shared through a cache and outlive the segment, so
      // only the per-sprite material is disposed here.
      if (object.isSprite) object.material.dispose();
    });
    state.markerGroup = null;
    state.markersByKind = null;
  }
}

function onPointerMove(event) {
  pointer.x = (event.clientX / window.innerWidth) * 2 - 1;
  pointer.y = -(event.clientY / window.innerHeight) * 2 + 1;

  raycaster.setFromCamera(pointer, camera);

  const sprites = [];
  state.markerGroup?.traverse((object) => {
    if (object.isSprite && object.visible && object.parent.visible) sprites.push(object);
  });

  const hit = raycaster.intersectObjects(sprites, false)[0];
  setHovered(hit ? hit.object : null, event.clientX, event.clientY);
}

function setHovered(sprite, x = 0, y = 0) {
  if (hovered === sprite) {
    if (sprite) positionTooltip(x, y);
    return;
  }

  hovered = sprite;

  if (!sprite) {
    tooltip.hidden = true;
    renderer.domElement.style.cursor = 'grab';
    return;
  }

  const { marker } = sprite.userData;
  const look = describe(marker);

  tooltip.innerHTML = '';
  tooltip.appendChild(row(look.label, 'tooltip-title'));
  if (look.variant) tooltip.appendChild(row(`Loot table: ${look.variant}`, 'tooltip-variant'));
  if (marker.name && marker.name !== look.label) tooltip.appendChild(row(marker.name, 'tooltip-name'));
  tooltip.appendChild(row(`Altitude ${Math.round(marker.pos[1])} m`, 'tooltip-meta'));
  tooltip.appendChild(row(marker.type, 'tooltip-type'));

  tooltip.hidden = false;
  renderer.domElement.style.cursor = 'pointer';
  positionTooltip(x, y);
}

function row(text, className) {
  const div = document.createElement('div');
  div.className = className;
  div.textContent = text;
  return div;
}

function positionTooltip(x, y) {
  tooltip.style.transform = `translate(${x + 16}px, ${y + 16}px)`;
}

function onResize() {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
}

function render() {
  controls.update();
  renderer.render(scene, camera);
}

function setStatus(text) {
  status.textContent = text;
  status.hidden = !text;
}

function reportFatal(error) {
  console.error(error);
  setStatus(`Failed to load: ${error.message}`);
}
