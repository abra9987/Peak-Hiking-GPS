using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;

namespace PeakMapInteractive.Minimap
{
    /// <summary>
    /// A live top-down view of the mountain, in a small window inside the game.
    ///
    /// This is the map the project was actually after. Every wall the exported
    /// map ran into disappears here: nothing has to be prised out of meshes the
    /// engine refuses to hand over, no shader has to be reproduced, nothing is
    /// shipped so nothing has a size budget, and fidelity is not approximated
    /// because the game draws its own world.
    ///
    /// It is built for route planning: north stays fixed upward so directions
    /// do not swim while the player turns, and the view can be tilted to read
    /// vertical relief, which on this mountain is most of the problem.
    /// </summary>
    internal sealed class MinimapController : MonoBehaviour
    {
        private const int Resolution = 512;
        private const int MaxMarkers = 64;
        private const float MarkerRefreshSeconds = 0.4f;

        /// <summary>
        /// Pitch presets. Straight down reads distance honestly; the tilted
        /// ones show how much climbing is between here and there, which a
        /// top-down view flattens away entirely.
        /// </summary>
        private static readonly float[] Pitches = { 90f, 75f, 45f };

        private Camera _camera;
        private RenderTexture _target;
        private Canvas _canvas;
        private RectTransform _panel;
        private RectTransform _playerMarker;
        private TextMeshProUGUI _readout;
        private RectTransform _compass;

        private float _lastAltitude;
        private float _climbRate;

        private readonly List<RectTransform> _markerPool = new List<RectTransform>();
        private readonly List<MarkerSighting> _sightings = new List<MarkerSighting>();
        private float _nextMarkerScan;

        private bool _wanted = true;
        private bool _shown;
        private float _span;
        private int _pitchIndex;

        private struct MarkerSighting
        {
            public Vector3 World;
            public Color Colour;
            public bool IsLoot;
        }

        private void Awake()
        {
            _span = Plugin.Settings.MinimapSpan.Value;
            BuildCamera();
            BuildOverlay();
            Show(false);
        }

        // --- construction ----------------------------------------------------

        private void BuildCamera()
        {
            var holder = new GameObject("PeakMapInteractive_MinimapCamera");
            holder.transform.SetParent(transform, worldPositionStays: false);

            _camera = holder.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.depth = -50f;   // never composites over the player's view
            _camera.cullingMask = WorldMask();

            _target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "PeakMapInteractive_Minimap",
                useMipMap = false,
                autoGenerateMips = false
            };
            _target.Create();
            _camera.targetTexture = _target;
        }

        /// <summary>
        /// Ground and structures only. Weather and effects would hang between
        /// the camera and the terrain, and a map you cannot see the ground
        /// through is not a map.
        /// </summary>
        private static int WorldMask()
        {
            int mask = 0;
            foreach (string name in new[] { "Terrain", "Map", "Default", "Vines" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0) mask |= 1 << layer;
            }

            return mask == 0 ? ~0 : mask;
        }

        private void BuildOverlay()
        {
            var canvasObject = new GameObject("PeakMapInteractive_MinimapCanvas");
            canvasObject.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;   // above the game's HUD
            canvasObject.AddComponent<CanvasScaler>();

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);

            _panel = panelObject.AddComponent<RectTransform>();
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-16f, -16f);

            float size = Plugin.Settings.MinimapSize.Value;
            _panel.sizeDelta = new Vector2(size, size);

            var image = panelObject.AddComponent<RawImage>();
            image.texture = _target;
            image.raycastTarget = false;

            _playerMarker = CreateArrow(panelObject.transform);
            _readout = CreateReadout(panelObject.transform);
            _compass = CreateCompass(panelObject.transform);
        }

        /// <summary>
        /// A needle that always points at the nearest loot, the way the game's
        /// own compass points at things rather than at north.
        ///
        /// It sits outside the map's edge so it keeps working when the loot is
        /// beyond the visible area — which is exactly when a direction is worth
        /// more than a dot.
        /// </summary>
        private static RectTransform CreateCompass(Transform parent)
        {
            var holder = new GameObject("LootCompass");
            holder.transform.SetParent(parent, worldPositionStays: false);

            var rect = holder.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -62f);
            rect.sizeDelta = new Vector2(26f, 26f);

            var image = holder.AddComponent<Image>();
            image.sprite = ArrowSprite();
            image.color = new Color(1f, 0.72f, 0.25f);
            image.raycastTarget = false;

            var outline = holder.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            return rect;
        }

        /// <summary>
        /// Altitude, and how fast it is changing, under the map.
        ///
        /// PEAK is a game about getting higher; a map that shows only where you
        /// are on the ground leaves out the axis the whole run is measured on.
        /// The trend arrow answers the question you actually ask mid-climb —
        /// am I still gaining height on this line, or has it flattened out.
        /// </summary>
        private static TextMeshProUGUI CreateReadout(Transform parent)
        {
            var textObject = new GameObject("Readout");
            textObject.transform.SetParent(parent, worldPositionStays: false);

            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            rect.sizeDelta = new Vector2(0f, 46f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = GameFont();
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.Top;
            text.color = new Color(0.94f, 0.94f, 0.92f);
            text.raycastTarget = false;
            text.enableWordWrapping = false;

            return text;
        }

        /// <summary>
        /// The game's own font, borrowed from whatever it has already loaded,
        /// so the overlay reads as part of PEAK rather than as something bolted
        /// on. Falls back to whatever TMP offers if that fails.
        /// </summary>
        private static TMP_FontAsset GameFont()
        {
            TMP_FontAsset font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(f => f != null && f.characterTable != null && f.characterTable.Count > 64);

            return font ?? TMP_Settings.defaultFontAsset;
        }

        /// <summary>
        /// The player is drawn as an arrow, not a dot or a model.
        ///
        /// At three hundred metres across, the character model is one or two
        /// pixels: neither the body nor the face would be legible, and both
        /// would cost the Character layer sitting between the camera and the
        /// ground. Which way you are facing is the part that actually helps
        /// when picking a line, so that is what the marker shows.
        /// </summary>
        private static RectTransform CreateArrow(Transform parent)
        {
            RectTransform rect = CreateDot(parent, "You", new Color(1f, 0.55f, 0.2f), 16f);
            rect.GetComponent<Image>().sprite = ArrowSprite();
            return rect;
        }

        private static Sprite _arrow;

        private static Sprite ArrowSprite()
        {
            if (_arrow != null) return _arrow;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // A triangle that narrows towards the top of the texture,
                    // with a notch cut out of the base so it reads as an arrow
                    // rather than a wedge at small sizes.
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float halfWidth = 0.5f * (1f - v);
                    bool inside = Mathf.Abs(u - 0.5f) <= halfWidth && v <= 1f;
                    bool notch = v < 0.28f && Mathf.Abs(u - 0.5f) < 0.5f * (0.28f - v) / 0.28f * 0.9f;

                    texture.SetPixel(x, y, inside && !notch ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            _arrow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _arrow;
        }

        /// <summary>
        /// A marker with a dark rim. Without one a yellow dot vanishes against
        /// sand and a purple one against shadow, which defeats the point of
        /// being able to spot loot from across the map.
        /// </summary>
        private static RectTransform CreateOutlinedDot(Transform parent, string name, float size)
        {
            RectTransform rect = CreateDot(parent, name, Color.white, size);

            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);

            return rect;
        }

        private static RectTransform CreateDot(Transform parent, string name, Color colour, float size)
        {
            var dot = new GameObject(name);
            dot.transform.SetParent(parent, worldPositionStays: false);

            var rect = dot.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            var image = dot.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            return rect;
        }

        // --- lifecycle -------------------------------------------------------

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.Settings.MinimapToggleKey.Value)) _wanted = !_wanted;

            bool ready = _wanted && IsInPlay();
            if (ready != _shown) Show(ready);
            if (!ready) return;

            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomInKey.Value)) SetSpan(_span * 0.7f);
            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomOutKey.Value)) SetSpan(_span / 0.7f);

            if (Input.GetKeyDown(Plugin.Settings.MinimapAngleKey.Value))
                _pitchIndex = (_pitchIndex + 1) % Pitches.Length;

            Follow();
            AimPlayerArrow();
            UpdateMarkers();
            UpdateReadout();
            UpdateCompass();
        }

        /// <summary>
        /// True only once the player is standing in a real biome.
        ///
        /// The airport has no map to show, and opening on it looked broken.
        /// The wake-up matters just as much: the run starts with the character
        /// lying on the beach and the eyelid effect washing the screen out, and
        /// the minimap was blinking along with it.
        /// </summary>
        private static bool IsInPlay()
        {
            if (LoadingScreenHandler.loading) return false;

            Character player = Character.localCharacter;
            if (player == null || player.data == null) return false;
            if (player.data.passedOut || player.data.fullyPassedOut) return false;
            if (player.data.passedOutOnTheBeach > 0f) return false;

            // The airport scene has no MapHandler, which is the cleanest way to
            // ask "am I on the mountain yet".
            MapHandler map = Singleton<MapHandler>.Instance;
            return map != null && map.segments != null && map.segments.Length > 0;
        }

        private void Show(bool shown)
        {
            _shown = shown;
            if (_camera != null) _camera.enabled = shown;
            if (_canvas != null) _canvas.enabled = shown;
        }

        private void SetSpan(float span)
        {
            _span = Mathf.Clamp(span, 20f, 2000f);
        }

        /// <summary>
        /// Places the camera above and behind the player at the chosen pitch,
        /// aimed at them, with north fixed upward.
        /// </summary>
        private void Follow()
        {
            Character player = Character.localCharacter;
            if (player == null || _camera == null) return;

            float pitch = Pitches[_pitchIndex];
            Vector3 focus = player.Center;

            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);

            // Step back along the camera's own view direction. Subtracting
            // instead put the camera 1200 m *under* the player, looking up
            // through the inside of the mountain at nothing at all.
            Vector3 back = rotation * Vector3.back * 1200f;

            _camera.transform.SetPositionAndRotation(focus + back, rotation);
            _camera.orthographicSize = _span * 0.5f;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 4000f;
        }

        /// <summary>
        /// Turns the arrow to match where the player is looking. The map is
        /// north-up, so the arrow carries all of the rotation.
        /// </summary>
        private void AimPlayerArrow()
        {
            if (_playerMarker == null) return;

            MainCamera view = MainCamera.instance;
            if (view == null) return;

            float yaw = view.transform.eulerAngles.y;
            _playerMarker.localRotation = Quaternion.Euler(0f, 0f, -yaw);
        }

        /// <summary>
        /// Writes altitude, whether it is rising, and how far the nearest bit
        /// of loot is.
        /// </summary>
        private void UpdateReadout()
        {
            Character player = Character.localCharacter;
            if (_readout == null || player == null) return;

            float altitude = player.Center.y;

            // Smoothed, because raw frame-to-frame change flickers between up
            // and down on every step and reads as noise.
            float delta = (altitude - _lastAltitude) / Mathf.Max(Time.deltaTime, 1e-4f);
            _climbRate = Mathf.Lerp(_climbRate, delta, 0.08f);
            _lastAltitude = altitude;

            string trend = _climbRate > 0.35f ? "<color=#7FE08A>^</color>"
                : _climbRate < -0.35f ? "<color=#E08A7F>v</color>"
                : "<color=#8A909A>-</color>";

            string line = $"{trend} {Mathf.RoundToInt(altitude)} m";

            float nearest = NearestLootDistance(player.Center);
            if (nearest < float.MaxValue)
                line += System.Environment.NewLine + $"<color=#FFB84A>loot {Mathf.RoundToInt(nearest)} m</color>";

            _readout.text = line;
        }

        /// <summary>
        /// Turns the needle towards the nearest loot, in the map's fixed
        /// north-up frame, and hides it when there is nothing to point at.
        /// </summary>
        private void UpdateCompass()
        {
            Character player = Character.localCharacter;
            if (_compass == null || player == null) return;

            if (!TryNearestLoot(player.Center, out Vector3 target, out float _))
            {
                _compass.gameObject.SetActive(false);
                return;
            }

            _compass.gameObject.SetActive(true);

            Vector3 delta = target - player.Center;
            // Bearing measured clockwise from north, which is what the map's
            // fixed orientation means by "up".
            float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            _compass.localRotation = Quaternion.Euler(0f, 0f, -bearing);
        }

        private bool TryNearestLoot(Vector3 from, out Vector3 position, out float distance)
        {
            position = Vector3.zero;
            distance = float.MaxValue;

            for (int i = 0; i < _sightings.Count; i++)
            {
                if (!_sightings[i].IsLoot) continue;

                float d = Vector3.Distance(from, _sightings[i].World);
                if (d >= distance) continue;

                distance = d;
                position = _sightings[i].World;
            }

            return distance < float.MaxValue;
        }

        private float NearestLootDistance(Vector3 from)
        {
            float best = float.MaxValue;

            for (int i = 0; i < _sightings.Count; i++)
            {
                if (!_sightings[i].IsLoot) continue;

                float distance = Vector3.Distance(from, _sightings[i].World);
                if (distance < best) best = distance;
            }

            return best;
        }

        // --- markers ---------------------------------------------------------

        /// <summary>
        /// Draws nearby chests, belltowers and the rest onto the view.
        ///
        /// Found by overlapping a sphere rather than by walking the scene:
        /// only what is within the map's own range can be shown anyway, and a
        /// full scan of a segment is tens of thousands of components.
        /// </summary>
        private void UpdateMarkers()
        {
            if (Time.time >= _nextMarkerScan)
            {
                _nextMarkerScan = Time.time + MarkerRefreshSeconds;
                ScanForMarkers();
            }

            int used = 0;

            for (int i = 0; i < _sightings.Count && used < MaxMarkers; i++)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(_sightings[i].World);
                if (viewport.z <= 0f) continue;
                if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;

                RectTransform dot = MarkerAt(used++);
                dot.gameObject.SetActive(true);
                dot.GetComponent<Image>().color = _sightings[i].Colour;

                Vector2 size = _panel.sizeDelta;
                dot.anchoredPosition = new Vector2(
                    (viewport.x - 0.5f) * size.x,
                    (viewport.y - 0.5f) * size.y);
            }

            for (int i = used; i < _markerPool.Count; i++)
                _markerPool[i].gameObject.SetActive(false);
        }

        private RectTransform MarkerAt(int index)
        {
            while (_markerPool.Count <= index)
                _markerPool.Add(CreateOutlinedDot(_panel, $"Marker{_markerPool.Count}", 13f));

            return _markerPool[index];
        }

        private void ScanForMarkers()
        {
            _sightings.Clear();

            Character player = Character.localCharacter;
            if (player == null) return;

            // Everyone else on the mountain, at any distance: knowing where
            // the others are is half of why a shared map is worth having.
            foreach (Character other in Character.AllCharacters)
            {
                if (other == null || other == player) continue;

                _sightings.Add(new MarkerSighting
                {
                    World = other.Center,
                    Colour = other.data != null && other.data.dead
                        ? new Color(0.55f, 0.55f, 0.6f)
                        : new Color(0.35f, 0.75f, 1f),
                    IsLoot = false
                });
            }

            Collider[] nearby = Physics.OverlapSphere(
                player.Center, _span, ~0, QueryTriggerInteraction.Collide);

            var seen = new HashSet<int>();

            foreach (Collider collider in nearby)
            {
                if (collider == null) continue;

                // A chest's collider sits on a child called something like
                // "Collider" while the name that identifies it is on a parent,
                // so testing the collider's own object found nothing at all.
                GameObject go = Identify(collider.gameObject, out Color colour, out bool isLoot);
                if (go == null) continue;
                if (!seen.Add(go.GetInstanceID())) continue;

                _sightings.Add(new MarkerSighting
                {
                    World = go.transform.position,
                    Colour = colour,
                    IsLoot = isLoot
                });
                if (_sightings.Count >= MaxMarkers) return;
            }
        }

        /// <summary>
        /// Recognises the things worth walking towards, by the same names the
        /// export uses, so both halves of the project agree on what counts.
        /// </summary>
        /// <summary>
        /// Finds the named object a collider belongs to, walking up until
        /// something recognisable turns up.
        /// </summary>
        private static GameObject Identify(GameObject from, out Color colour, out bool isLoot)
        {
            Transform current = from.transform;

            for (int depth = 0; depth < 5 && current != null; depth++)
            {
                if (TryClassify(current.gameObject, out colour, out isLoot))
                    return current.gameObject;

                current = current.parent;
            }

            colour = default;
            isLoot = false;
            return null;
        }

        private static bool TryClassify(GameObject go, out Color colour, out bool isLoot)
        {
            string name = go.name.ToLowerInvariant();
            isLoot = false;

            if (name.Contains("spawner") || name.Contains("sfx") || name.Contains("trigger")
                || name.Contains("ambience") || name.Contains("zone"))
            {
                colour = default;
                return false;
            }

            // Loot is what the distance readout tracks: things worth a detour.
            if (name.Contains("luggage")) { colour = new Color(1f, 0.72f, 0.25f); isLoot = true; return true; }
            if (name.Contains("amulet")) { colour = new Color(0.3f, 1f, 0.55f); isLoot = true; return true; }

            if (name.Contains("bell")) { colour = new Color(0.72f, 0.45f, 1f); return true; }
            if (name.Contains("campfire")) { colour = new Color(1f, 0.45f, 0.2f); return true; }
            if (name.Contains("tomb")) { colour = new Color(0.7f, 0.75f, 0.8f); return true; }
            if (name.Contains("statue")) { colour = new Color(0.6f, 0.6f, 0.65f); return true; }

            colour = default;
            return false;
        }

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;

            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
        }
    }
}
