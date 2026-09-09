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
        private RectTransform _frame;
        private RectTransform _panel;
        private RectTransform _compass;
        private RectTransform _playerMarker;
        private TextMeshProUGUI _readout;

        private float _lastAltitude;
        private float _climbRate;

        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<MarkerSighting> _sightings = new List<MarkerSighting>();
        private float _nextMarkerScan;

        private bool _wanted = true;
        private bool _shown;

        /// <summary>When the mountain finished loading, and whether the run has begun.</summary>
        private float _levelSince = -1f;
        private bool _stoodUp;
        private float _span;
        private int _pitchIndex;

        private struct MarkerSighting
        {
            public Vector3 World;
            public Color Colour;
            public bool IsLoot;

            /// <summary>What kind of thing this is, for looking up its icon.</summary>
            public string IconKey;

            /// <summary>
            /// The object itself, so an icon can be baked from its model the
            /// first time one of these is drawn. Null for anything drawn as a
            /// plain marker, such as the other climbers.
            /// </summary>
            public GameObject Source;
        }

        /// <summary>
        /// One marker on the map: a coloured plate carrying the category and
        /// the height reading, and, once it has been baked, a picture of the
        /// thing itself on top of it.
        ///
        /// Two layers rather than one because they answer different questions.
        /// The picture says what it is; the plate says whether it is above you
        /// or below, and gives the picture something to stand out against when
        /// the ground underneath is sand or snow.
        /// </summary>
        private sealed class Marker
        {
            public RectTransform Root;
            public Image Plate;
            public Image Icon;
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

            float size = Plugin.Settings.MinimapSize.Value;
            const float border = 7f;
            const float readoutHeight = 46f;

            // A frame around the whole thing, with the numbers inside it. Loose
            // elements floating over the world read as debug output; a bordered
            // panel reads as part of the game's interface.
            var frameObject = new GameObject("Frame");
            frameObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);

            _frame = frameObject.AddComponent<RectTransform>();
            _frame.anchorMin = _frame.anchorMax = new Vector2(1f, 1f);
            _frame.pivot = new Vector2(1f, 1f);
            _frame.anchoredPosition = new Vector2(-18f, -18f);
            _frame.sizeDelta = new Vector2(size + border * 2f, size + border * 2f + readoutHeight);

            var frameImage = frameObject.AddComponent<Image>();
            frameImage.color = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            frameImage.raycastTarget = false;

            var frameOutline = frameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(0.75f, 0.66f, 0.48f, 0.85f);
            frameOutline.effectDistance = new Vector2(2f, -2f);

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(frameObject.transform, worldPositionStays: false);

            _panel = panelObject.AddComponent<RectTransform>();
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = new Vector2(0f, -border);
            _panel.sizeDelta = new Vector2(size, size);

            var image = panelObject.AddComponent<RawImage>();
            image.texture = _target;
            image.raycastTarget = false;

            _playerMarker = CreateArrow(panelObject.transform);
            _readout = CreateReadout(frameObject.transform);
            _compass = CreateCompass(panelObject.transform);
        }

        /// <summary>
        /// The game's own compass item, in the corner of the map, turning
        /// towards the nearest chest.
        ///
        /// Using the actual item icon rather than a drawn arrow is the point:
        /// a player who has held that compass knows instantly what it does, so
        /// the overlay needs no explaining.
        /// </summary>
        private static RectTransform CreateCompass(Transform parent)
        {
            var holder = new GameObject("Compass");
            holder.transform.SetParent(parent, worldPositionStays: false);

            var rect = holder.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(8f, 8f);
            rect.sizeDelta = new Vector2(64f, 64f);

            var image = holder.AddComponent<Image>();
            image.sprite = CompassSprite() ?? ArrowSprite();
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = true;

            return rect;
        }

        private static Sprite _compassSprite;

        /// <summary>
        /// Pulls the compass icon out of the item the game has already loaded.
        /// Nothing is copied into the mod, so no artwork is redistributed.
        /// </summary>
        private static Sprite CompassSprite()
        {
            if (_compassSprite != null) return _compassSprite;

            foreach (Item item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item == null || item.UIData == null) continue;

                string name = item.UIData.itemName ?? item.name ?? string.Empty;
                if (name.IndexOf("compass", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                Texture2D icon = item.UIData.GetIcon();
                if (icon == null) continue;

                _compassSprite = Sprite.Create(
                    icon, new Rect(0f, 0f, icon.width, icon.height), new Vector2(0.5f, 0.5f));
                return _compassSprite;
            }

            return null;
        }

        /// <summary>
        /// Altitude and the nearest chest, under the map.
        ///
        /// PEAK is scored on height, and a map is the one view that hides it:
        /// looking down flattens away the only axis the run is about.
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
            rect.sizeDelta = new Vector2(0f, 52f);

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
        /// The game's own font, borrowed from what it has already loaded, so
        /// the overlay reads as part of PEAK rather than as something bolted on.
        /// </summary>
        private static TMP_FontAsset GameFont()
        {
            TMP_FontAsset font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(f => f != null && f.characterTable != null && f.characterTable.Count > 64);

            return font ?? TMP_Settings.defaultFontAsset;
        }

        /// <summary>
        /// The player is drawn as an arrow, not a dot or a model: at this scale
        /// a character is a pixel or two, and which way you face is the part
        /// that helps when picking a line.
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
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float halfWidth = 0.5f * (1f - v);
                    bool inside = Mathf.Abs(u - 0.5f) <= halfWidth;
                    bool notch = v < 0.28f && Mathf.Abs(u - 0.5f) < 0.5f * (0.28f - v) / 0.28f * 0.9f;

                    texture.SetPixel(x, y, inside && !notch ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            _arrow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _arrow;
        }

        /// <summary>
        /// Builds an empty marker: a round plate, and an icon slot above it
        /// that stays hidden until a picture has been baked for whatever the
        /// marker turns out to be.
        ///
        /// Both layers carry a dark rim. Without one a yellow plate vanishes
        /// against sand and a brown suitcase against mud, which defeats the
        /// point of being able to spot loot from across the map.
        /// </summary>
        private static Marker CreateMarker(Transform parent, string name)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, worldPositionStays: false);

            var root = holder.AddComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            Image plate = CreateLayer(root, "Plate", Vector2.zero, Vector2.one);
            plate.sprite = DiscSprite();

            // Inset, so the plate reads as a ring around the picture rather
            // than as a background the picture is glued onto.
            Image icon = CreateLayer(root, "Icon", new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
            icon.preserveAspect = true;
            icon.gameObject.SetActive(false);

            return new Marker { Root = root, Plate = plate, Icon = icon };
        }

        private static Image CreateLayer(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, worldPositionStays: false);

            var rect = holder.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = holder.AddComponent<Image>();
            image.raycastTarget = false;

            var outline = holder.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);

            return image;
        }

        private static Sprite _disc;

        /// <summary>
        /// A circle, drawn rather than shipped. The markers were squares until
        /// now — the default UI sprite — which read as debug output next to
        /// anything the game draws itself.
        /// </summary>
        private static Sprite DiscSprite()
        {
            if (_disc != null) return _disc;

            const int size = 64;
            const float radius = size * 0.5f - 1f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var centre = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);

                    // One pixel of feather, so the rim is a circle and not a
                    // staircase at the sizes these are actually drawn at.
                    float alpha = Mathf.Clamp01(radius - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            _disc = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _disc;
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
        /// True only once the run has actually started — the character is up on
        /// their feet, not still lying on the beach with their eyes shut.
        ///
        /// Two conditions, and the later one wins. The first is the game's own
        /// idea of the run having begun, taken from what
        /// <c>Character.TestSpawnChallengeItems</c> waits for before it puts an
        /// item in your hand: not passed out on the beach, not being warped,
        /// standing on something. The second is a plain delay from the moment
        /// the mountain finished loading, because "grounded" goes true the
        /// instant the body touches sand, well before the character has got up
        /// and rubbed their eyes.
        ///
        /// Getting this wrong is not cosmetic. Icons are photographed out of
        /// the loaded scene and kept for the rest of the run, so one taken from
        /// a world that is still assembling itself is wrong until the run ends.
        /// </summary>
        private bool IsInPlay()
        {
            if (LoadingScreenHandler.loading) return Restart();

            Character player = Character.localCharacter;
            if (player == null || player.data == null) return Restart();

            // The airport scene has no MapHandler, which is the cleanest way to
            // ask "am I on the mountain yet".
            MapHandler map = Singleton<MapHandler>.Instance;
            if (map == null || map.segments == null || map.segments.Length == 0) return Restart();

            if (_levelSince < 0f) _levelSince = Time.time;

            // Passing out later in the run hides the map, but does not send the
            // wake-up conditions back to the start: they have already been met.
            if (player.data.passedOut || player.data.fullyPassedOut) return false;

            if (!_stoodUp)
            {
                if (player.data.passedOutOnTheBeach > 0f) return false;
                if (player.warping) return false;
                if (!player.data.isGrounded) return false;
                if (Time.time - _levelSince < Plugin.Settings.MinimapStartDelay.Value) return false;

                _stoodUp = true;
            }

            return true;
        }

        /// <summary>Forgets the run, for when there is no longer one to be in.</summary>
        private bool Restart()
        {
            _levelSince = -1f;
            _stoodUp = false;
            return false;
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

            if (TryNearestLoot(player.Center, out Vector3 chest, out float _))
            {
                Vector3 toChest = chest - player.Center;

                // Split into ground distance and height on purpose. Thirty
                // metres sideways and thirty metres up are nothing alike here:
                // one is a walk, the other may have no route at all, and a
                // single straight-line number hides which you are looking at.
                int across = Mathf.RoundToInt(new Vector2(toChest.x, toChest.z).magnitude);
                int up = Mathf.RoundToInt(toChest.y);

                string height = up > 2 ? $"<color=#7FE08A>+{up} m</color>"
                    : up < -2 ? $"<color=#E08A7F>{up} m</color>"
                    : "<color=#8A909A>level</color>";

                line += System.Environment.NewLine +
                        $"<color=#FFB84A>chest {across} m</color>  {height}";
            }

            _readout.text = line;
        }

        /// <summary>
        /// Points the compass at the nearest chest, relative to where the
        /// player is facing, the way a compass held in the hand behaves.
        /// </summary>
        private void UpdateCompass()
        {
            Character player = Character.localCharacter;
            if (_compass == null || player == null) return;

            // Always on screen. It used to disappear whenever there was no
            // chest in range, which is exactly when a player looks at it to
            // work out which way they are facing — and a control that vanishes
            // reads as broken rather than as empty.
            //
            // With nothing to point at it points north, which is what a compass
            // does when it is not being asked anything.
            bool hasTarget = TryNearestLoot(player.Center, out Vector3 target, out float _);

            Vector3 delta = hasTarget ? target - player.Center : Vector3.forward;
            float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float facing = MainCamera.instance != null
                ? MainCamera.instance.transform.eulerAngles.y
                : 0f;

            // The needle is painted into the icon rather than being a separate
            // part, so the whole thing turns — hat and all, which reads as the
            // compass being turned in hand. The offset accounts for where that
            // painted needle already points, without which it would aim wide
            // by a fixed angle forever.
            float painted = Plugin.Settings.MinimapCompassNeedleOffset.Value;
            _compass.localRotation = Quaternion.Euler(0f, 0f, -((bearing - facing) - painted));
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

            Character self = Character.localCharacter;
            float baseSize = Plugin.Settings.MinimapMarkerSize.Value;
            bool wantIcons = Plugin.Settings.MinimapIcons.Value;
            Vector2 panel = _panel.sizeDelta;
            int used = 0;

            for (int i = 0; i < _sightings.Count && used < MaxMarkers; i++)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(_sightings[i].World);
                if (viewport.z <= 0f) continue;
                if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;

                Marker marker = MarkerAt(used++);
                marker.Root.gameObject.SetActive(true);
                marker.Root.anchoredPosition = new Vector2(
                    (viewport.x - 0.5f) * panel.x,
                    (viewport.y - 0.5f) * panel.y);

                float height = self != null
                    ? Mathf.Clamp((_sightings[i].World.y - self.Center.y) / 60f, -1f, 1f)
                    : 0f;

                float size = baseSize * Mathf.Lerp(0.7f, 1.25f, (height + 1f) * 0.5f);
                marker.Root.sizeDelta = new Vector2(size, size);
                marker.Plate.color = ShadeByHeight(_sightings[i].Colour, height);

                Sprite icon = wantIcons ? IconFor(_sightings[i]) : null;
                marker.Icon.gameObject.SetActive(icon != null);

                if (icon == null) continue;

                marker.Icon.sprite = icon;
                marker.Icon.color = ShadeIconByHeight(height);
            }

            for (int i = used; i < _markers.Count; i++)
                _markers[i].Root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The picture for a marker, asking for one to be baked the first time
        /// this kind of thing is drawn.
        ///
        /// Baking is deliberately driven from here rather than from the scan:
        /// only what is actually on screen is worth photographing, and the scan
        /// reaches further than the window does.
        /// </summary>
        private static Sprite IconFor(MarkerSighting sighting)
        {
            if (string.IsNullOrEmpty(sighting.IconKey)) return null;

            Sprite baked = IconBaker.Get(sighting.IconKey);
            if (baked == null) IconBaker.Request(sighting.IconKey, sighting.Source);

            return baked;
        }

        /// <summary>
        /// Shades a marker by how far above or below the player it sits, and
        /// sizes it the same way.
        ///
        /// This is the one thing a top-down map cannot say on its own, and in
        /// PEAK it is the thing that matters: a chest thirty metres away on the
        /// flat is a short walk, the same chest thirty metres up may have no
        /// route to it at all. Looking at the map and guessing wrong about
        /// which one you are seeing is the mistake worth designing out.
        ///
        /// Higher reads lighter and larger, lower reads darker and smaller,
        /// the way distance already reads on any map.
        /// </summary>
        private static Color ShadeByHeight(Color colour, float height)
            => height >= 0f
                ? Color.Lerp(colour, Color.white, height * 0.65f)
                : Color.Lerp(colour, new Color(0.16f, 0.14f, 0.18f), -height * 0.7f);

        /// <summary>
        /// The same reading applied to the picture, but only downwards.
        ///
        /// An <see cref="Image"/> tint multiplies, so lightening a photograph
        /// towards white only washes it out, and the plate underneath already
        /// says "above you" clearly enough. Darkening still works, and things
        /// below you receding into shadow is what the eye expects anyway.
        /// </summary>
        private static Color ShadeIconByHeight(float height)
            => height >= 0f
                ? Color.white
                : Color.Lerp(Color.white, new Color(0.5f, 0.47f, 0.55f), -height);

        private Marker MarkerAt(int index)
        {
            while (_markers.Count <= index)
                _markers.Add(CreateMarker(_panel, $"Marker{_markers.Count}"));

            return _markers[index];
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

                // No icon: a scout is a different scout every run, and the
                // model is customised per player, so a photograph of one would
                // be a picture of somebody in particular rather than of a
                // category. The colour already says alive or dead.
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

                // Chests are found by their component rather than by name.
                // Name matching also caught every loose pickup lying around,
                // and the map is meant to show what is worth walking to, not
                // every object in the world.
                Luggage chest = collider.GetComponentInParent<Luggage>();
                if (chest != null)
                {
                    // An opened chest has already been looted; leaving it on
                    // the map sends you to something with nothing in it.
                    if (chest.IsOpen) continue;
                    if (!seen.Add(chest.gameObject.GetInstanceID())) continue;

                    _sightings.Add(new MarkerSighting
                    {
                        World = chest.transform.position,
                        Colour = new Color(1f, 0.72f, 0.25f),
                        IsLoot = true,
                        IconKey = IconBaker.KeyFor(chest.gameObject),
                        Source = chest.gameObject
                    });

                    if (_sightings.Count >= MaxMarkers) return;
                    continue;
                }

                if (TryCreature(collider, out GameObject creature, out Color creatureColour))
                {
                    if (!seen.Add(creature.GetInstanceID())) continue;

                    _sightings.Add(new MarkerSighting
                    {
                        World = creature.transform.position,
                        Colour = creatureColour,
                        IsLoot = false,
                        IconKey = IconBaker.KeyFor(creature),
                        Source = creature
                    });

                    if (_sightings.Count >= MaxMarkers) return;
                    continue;
                }

                GameObject go = Identify(collider.gameObject, out Color colour, out bool isLoot);
                if (go == null) continue;
                if (!seen.Add(go.GetInstanceID())) continue;

                _sightings.Add(new MarkerSighting
                {
                    World = go.transform.position,
                    Colour = colour,
                    IsLoot = isLoot,
                    IconKey = IconBaker.KeyFor(go),
                    Source = go
                });
                if (_sightings.Count >= MaxMarkers) return;
            }
        }

        /// <summary>
        /// The living things on the mountain, found by component the way chests
        /// are: a capybara is worth walking towards and a scoutmaster is worth
        /// walking away from, and both are the kind of thing a picture says
        /// instantly and a coloured dot never could.
        /// </summary>
        private static bool TryCreature(Collider collider, out GameObject go, out Color colour)
        {
            var capybara = collider.GetComponentInParent<Capybara>();
            if (capybara != null)
            {
                go = capybara.gameObject;
                colour = new Color(0.85f, 0.62f, 0.35f);
                return true;
            }

            var scoutmaster = collider.GetComponentInParent<Scoutmaster>();
            if (scoutmaster != null)
            {
                go = scoutmaster.gameObject;
                colour = new Color(0.95f, 0.25f, 0.3f);
                return true;
            }

            // Crabs and jellyfish. They are hazards rather than landmarks, but
            // knowing one is on the ledge above changes the line you pick.
            var mob = collider.GetComponentInParent<Mob>();
            if (mob != null)
            {
                go = mob.gameObject;
                colour = new Color(0.6f, 0.85f, 0.55f);
                return true;
            }

            go = null;
            colour = default;
            return false;
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

            // Chests are handled by component above. What is left here are
            // landmarks: fixed things worth steering by, never loose pickups.
            if (name.Contains("luggage")) { colour = default; return false; }

            if (name.Contains("bell")) { colour = new Color(0.72f, 0.45f, 1f); return true; }
            if (name.Contains("campfire")) { colour = new Color(1f, 0.45f, 0.2f); return true; }
            if (name.Contains("tomb")) { colour = new Color(0.7f, 0.75f, 0.8f); return true; }
            if (name.Contains("statue")) { colour = new Color(0.6f, 0.6f, 0.65f); return true; }
            if (name.Contains("scout statue")) { colour = new Color(0.6f, 0.6f, 0.65f); return true; }

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
