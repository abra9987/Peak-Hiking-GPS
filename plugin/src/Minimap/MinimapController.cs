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

        /// <summary>
        /// The map is rendered at the shape of the screen it is shown on, not
        /// as a square. The navigator's screen is slightly wider than it is
        /// tall, and a square texture stretched into it would lean every
        /// slope on the mountain.
        /// </summary>
        private static int TextureHeight => Mathf.RoundToInt(Resolution / Navigator.ScreenAspect);
        private const int MaxMarkers = 64;
        private const float MarkerRefreshSeconds = 0.4f;

        /// <summary>
        /// Pitch presets. Straight down reads distance honestly; the tilted
        /// ones show how much climbing is between here and there, which a
        /// top-down view flattens away entirely.
        /// </summary>
        private static readonly float[] Pitches = { 90f, 75f, 45f };

        /// <summary>
        /// How far the marker scan reaches when the window is tighter than
        /// this. The map can only draw what is in the window, but the compass
        /// and the readout name the nearest chest, and those want to see past
        /// the edge of the picture.
        /// </summary>
        private const float ScanRange = 250f;

        /// <summary>
        /// The zoom rungs, tightest first.
        ///
        /// Zooming used to multiply whatever the span happened to be, which
        /// meant there was no such thing as a step: where you ended up depended
        /// on where you started and how many times you had pressed the key.
        /// A fixed ladder makes "three rungs up from the tightest" a real place,
        /// which is what a starting zoom has to be to be worth configuring.
        /// </summary>
        private static readonly float[] Spans = BuildSpans();

        private static float[] BuildSpans()
        {
            var spans = new List<float>();

            // The same 0.7 the zoom keys always used, so the feel of one press
            // is unchanged; only the places it can land are now fixed.
            for (float span = 20f; span < 2000f; span /= 0.7f)
                spans.Add(Mathf.Round(span));

            spans.Add(2000f);
            return spans.ToArray();
        }

        private Camera _camera;
        private RenderTexture _target;
        private Canvas _canvas;
        private RectTransform _frame;
        private RectTransform _panel;
        private RectTransform _markerLayer;
        private RectTransform _playerMarker;
        private TextMeshProUGUI _readout;

        private readonly RectTransform[] _buttons = new RectTransform[3];
        private readonly Image[] _buttonFaces = new Image[3];
        private readonly float[] _pressedUntil = new float[3];

        private float _lastAltitude;
        private float _climbRate;

        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<MarkerSighting> _sightings = new List<MarkerSighting>();
        private float _nextMarkerScan;

        private bool _wanted = true;
        private bool _shown;

        /// <summary>
        /// Shows the map regardless of whether the run is playable.
        ///
        /// Only ever set by the unattended icon run, which drives the game
        /// itself and leaves the character half-spawned under the terrain — so
        /// every honest test of "is this person actually playing" says no. The
        /// map draws fine from there, and a photograph of it is the only way to
        /// see the markers without somebody climbing to them.
        /// </summary>
        internal static bool ForceVisible;

        /// <summary>When the mountain finished loading, and whether the run has begun.</summary>
        private float _levelSince = -1f;
        private bool _stoodUp;
        private int _zoom;
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
            _zoom = StartZoom();
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

            _target = new RenderTexture(Resolution, TextureHeight, 24, RenderTextureFormat.ARGB32)
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

            // The device is one drawing on a square canvas, and everything on
            // it — screen, buttons — is placed as a fraction of that square. So
            // there is a single number to size it by, and nothing that has to
            // be kept in step by hand.
            var deviceObject = new GameObject("Navigator");
            deviceObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);

            _frame = deviceObject.AddComponent<RectTransform>();
            _frame.sizeDelta = new Vector2(size * Navigator.Aspect, size);
            PinToCorner(_frame);

            // The screen is added first so that the case and the glass drawn
            // after it land on top: UI draws in the order things were added.
            var screenObject = new GameObject("Screen");
            screenObject.transform.SetParent(deviceObject.transform, worldPositionStays: false);

            _panel = screenObject.AddComponent<RectTransform>();
            Fill(_panel, Navigator.ScreenArea);

            var map = screenObject.AddComponent<RawImage>();
            map.texture = _target;
            map.raycastTarget = false;

            // Markers live on a layer of their own, added before the numbers.
            // They are created as things come into range, which is to say
            // later than everything built here — and UI draws in the order
            // things were added, so without a layer to sit in, every chest
            // that came along was drawn over the altitude.
            var markerObject = new GameObject("Markers");
            markerObject.transform.SetParent(screenObject.transform, worldPositionStays: false);

            _markerLayer = markerObject.AddComponent<RectTransform>();
            Fill(_markerLayer, Rect.MinMaxRect(0f, 0f, 1f, 1f));

            _playerMarker = CreateArrow(markerObject.transform);
            _readout = CreateReadout(screenObject.transform, size);

            Cover(deviceObject.transform, "Glass", Navigator.Glass);
            Cover(deviceObject.transform, "Body", Navigator.Body);

            // Buttons last, so they sit on the case rather than in it.
            //
            // The recesses are holes straight through the drawing, so a face
            // that merely fits one leaves its corners open to the mountain
            // behind. Putting the faces behind the case closed that and cost
            // the thing it was for: framed by the hole, a button reads as
            // sunken. Drawn on top and larger than its hole, it covers the hole
            // outright and its own rim lands on the case, which is what a
            // button standing proud of a panel looks like.
            BuildButtons(deviceObject.transform);

            if (!Navigator.Available)
                Plugin.Logger.LogWarning("Minimap: the navigator artwork did not load; the map is on its own.");
        }

        /// <summary>
        /// How much wider than its recess a button face is drawn, and how far
        /// it shrinks when pressed.
        ///
        /// Both states stay larger than the hole, because the hole goes
        /// straight through the drawing and anything the face does not cover
        /// shows the mountain through it. Pressed is 1.06 of the hole and at
        /// rest 1.14, so the overhang shrinks by more than half — the face
        /// visibly settles towards the panel without ever uncovering it.
        /// </summary>
        private const float Overlap = 0.14f;
        private const float PressedScale = 0.93f;

        /// <summary>Grows a rect outward by a fraction of its own size.</summary>
        private static Rect Grow(Rect area, float by)
            => Rect.MinMaxRect(
                area.xMin - area.width * by,
                area.yMin - area.height * by,
                area.xMax + area.width * by,
                area.yMax + area.height * by);

        /// <summary>
        /// Hangs the device in whichever corner was asked for.
        ///
        /// Anchor, pivot and the sign of the margin all follow from the corner,
        /// which is why they are worked out together rather than written out
        /// four times.
        /// </summary>
        private static void PinToCorner(RectTransform rect)
        {
            ScreenCorner corner = Plugin.Settings.MinimapCorner.Value;

            bool right = corner == ScreenCorner.TopRight || corner == ScreenCorner.BottomRight;
            bool top = corner == ScreenCorner.TopRight || corner == ScreenCorner.TopLeft;

            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;

            rect.anchoredPosition = new Vector2(
                (right ? -1f : 1f) * Plugin.Settings.MinimapMarginX.Value,
                (top ? -1f : 1f) * Plugin.Settings.MinimapMarginY.Value);
        }

        /// <summary>Stretches a rect across a fraction of its parent.</summary>
        private static void Fill(RectTransform rect, Rect area)
        {
            rect.anchorMin = area.min;
            rect.anchorMax = area.max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// One full-size layer of the device drawing. Skipped when its artwork
        /// is missing, rather than drawn as a white rectangle.
        /// </summary>
        private static void Cover(Transform parent, string name, Sprite sprite)
        {
            if (sprite == null) return;

            var holder = new GameObject(name);
            holder.transform.SetParent(parent, worldPositionStays: false);

            Fill(holder.AddComponent<RectTransform>(), Rect.MinMaxRect(0f, 0f, 1f, 1f));

            var image = holder.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
        }

        /// <summary>
        /// The three buttons, sitting in the recesses drawn for them.
        ///
        /// They cannot be clicked — during a run the cursor belongs to the game
        /// — so they are not controls. They are there because a device has
        /// buttons, and because a keypress ought to be acknowledged by
        /// something other than the scale silently changing.
        /// </summary>
        private void BuildButtons(Transform parent)
        {
            for (int index = 0; index < Navigator.ButtonCount && index < _buttons.Length; index++)
            {
                Sprite face = Navigator.Button(index);
                if (face == null) continue;

                var holder = new GameObject("Button" + index);
                holder.transform.SetParent(parent, worldPositionStays: false);

                var rect = holder.AddComponent<RectTransform>();
                Fill(rect, Grow(Navigator.ButtonArea(index), Overlap));

                var image = holder.AddComponent<Image>();
                image.sprite = face;
                image.raycastTarget = false;

                // Filling the recess, not fitted inside it. A face fitted while
                // keeping its own proportions sat in the middle of its recess
                // at about seventy per cent, which reads as a button that has
                // come loose. The faces are a little squarer than the recesses,
                // so filling stretches them by about a sixth — invisible on a
                // plus and a minus, and worth it to make the button look
                // seated.
                image.preserveAspect = false;

                _buttons[index] = rect;
                _buttonFaces[index] = image;
            }
        }

        /// <summary>A plain coloured rectangle, positioned as a fraction of its parent.</summary>
        private static RectTransform Panel(Transform parent, string name, Rect area, Color colour)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, worldPositionStays: false);

            var rect = holder.AddComponent<RectTransform>();
            Fill(rect, area);

            var image = holder.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            return rect;
        }

        /// <summary>
        /// Presses a button, for about as long as a press looks like it lasts.
        ///
        /// A second drawing per button would be better and does not exist yet,
        /// so the face shrinks into its recess and darkens, which is what a
        /// button being pushed in looks like from directly above. Short on
        /// purpose: a held zoom key repeats faster than a long animation could
        /// finish, and a face still moving after the scale has changed twice
        /// reads as lag rather than as feedback.
        /// </summary>
        private void Press(int index)
        {
            if (index < 0 || index >= _buttons.Length) return;

            _pressedUntil[index] = Time.unscaledTime + 0.09f;
        }

        private void UpdateButtons()
        {
            for (int index = 0; index < _buttons.Length; index++)
            {
                if (_buttons[index] == null) continue;

                bool down = Time.unscaledTime < _pressedUntil[index];

                _buttons[index].localScale = Vector3.one * (down ? PressedScale : 1f);
                _buttonFaces[index].color = down ? new Color(0.74f, 0.74f, 0.76f) : Color.white;
            }
        }

        /// <summary>
        /// Altitude and the nearest chest, under the map.
        ///
        /// PEAK is scored on height, and a map is the one view that hides it:
        /// looking down flattens away the only axis the run is about.
        /// </summary>
        private static TextMeshProUGUI CreateReadout(Transform parent, float deviceSize)
        {
            // A dark strip along the bottom of the screen, the way a handheld
            // unit puts its numbers under the map. On the screen rather than on
            // the case: the case is a solid moulded object, and printing on it
            // would read as a sticker.
            var stripObject = new GameObject("Status");
            stripObject.transform.SetParent(parent, worldPositionStays: false);

            var strip = stripObject.AddComponent<RectTransform>();
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0.155f);
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = Vector2.zero;

            var backing = stripObject.AddComponent<Image>();
            backing.color = new Color(0.05f, 0.06f, 0.08f, 0.72f);
            backing.raycastTarget = false;

            var textObject = new GameObject("Readout");
            textObject.transform.SetParent(stripObject.transform, worldPositionStays: false);

            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = GameFont();

            // Sized to whatever fits. The line is short at the beach and long
            // in the Citadel, and at a fixed size the long version ran off the
            // screen and printed across the case.
            text.enableAutoSizing = true;
            text.fontSizeMin = 6f;
            text.fontSizeMax = Mathf.Max(9f, deviceSize * 0.05f);
            text.alignment = TextAlignmentOptions.Center;
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
            RectTransform rect = CreateDot(parent, "You", PlayerColour(), 16f);
            rect.GetComponent<Image>().sprite = ArrowSprite();
            return rect;
        }

        /// <summary>
        /// The colour of the arrow that is you, from the config. Falls back to
        /// the original orange rather than to nothing, so a typo in the hex
        /// costs a colour and not the marker.
        /// </summary>
        private static Color PlayerColour()
        {
            string wanted = Plugin.Settings.MinimapPlayerColour.Value;

            if (!string.IsNullOrWhiteSpace(wanted) && ColorUtility.TryParseHtmlString(wanted.Trim(), out Color colour))
                return colour;

            Plugin.Logger.LogWarning($"Minimap: '{wanted}' is not a colour; using the default.");
            return new Color(1f, 0.55f, 0.2f);
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

            bool ready = _wanted && (ForceVisible || IsInPlay());
            if (ready != _shown) Show(ready);
            if (!ready) return;

            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomInKey.Value))
            {
                _zoom = Mathf.Max(_zoom - 1, 0);
                Press(2);
            }

            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomOutKey.Value))
            {
                _zoom = Mathf.Min(_zoom + 1, Spans.Length - 1);
                Press(0);
            }

            if (Input.GetKeyDown(Plugin.Settings.MinimapAngleKey.Value))
            {
                _pitchIndex = (_pitchIndex + 1) % Pitches.Length;
                Press(1);
            }

            if (Input.GetKeyDown(Plugin.Settings.MinimapBakeAllKey.Value)) BakeEverything();

            Follow();
            AimPlayerArrow();
            UpdateMarkers();
            UpdateButtons();
            UpdateReadout();
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
            // Opening always starts from the configured rung. Coming back to a
            // zoom left over from the last time it was open is disorienting:
            // the map is glanced at mid-climb, and a glance has no time to work
            // out what scale it is looking at.
            if (shown && !_shown) _zoom = StartZoom();

            _shown = shown;
            if (_camera != null) _camera.enabled = shown;
            if (_canvas != null) _canvas.enabled = shown;
        }

        /// <summary>How many metres across the window currently covers.</summary>
        private float Span => Spans[_zoom];

        /// <summary>
        /// Which rung the map opens on, counting the tightest as the first.
        /// </summary>
        private static int StartZoom()
            => Mathf.Clamp(Plugin.Settings.MinimapStartZoom.Value - 1, 0, Spans.Length - 1);

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
            _camera.orthographicSize = Span * 0.5f;
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

                // One line, not two. The strip is cut out of the map, and a
                // second line of it cost twice the view to say something a
                // glance reads across in one pass anyway.
                line += $"   <color=#FFB84A>chest {across} m</color> {height}";
            }

            _readout.text = line;
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
            Vector2 panel = _panel.rect.size;
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
                _markers.Add(CreateMarker(_markerLayer, $"Marker{_markers.Count}"));

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

            // Deliberately wider than the window. What is drawn is decided by
            // the viewport test below, but the compass and the readout both ask
            // for the nearest chest, and at the tightest zoom a scan the size of
            // the window would leave them pointing at nothing forty metres from
            // a suitcase.
            Collider[] nearby = Physics.OverlapSphere(
                player.Center, Mathf.Max(Span, ScanRange), ~0, QueryTriggerInteraction.Collide);

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
        /// Photographs every kind of thing anywhere on the loaded mountain, in
        /// one go, instead of waiting to walk past one of each.
        ///
        /// Icons are normally baked on first sighting, which is right for
        /// playing and hopeless for working on them: the nearest statue can be
        /// five minutes of climbing away, and that is five minutes per attempt
        /// at getting a statue to look right. The whole level is already in
        /// memory, so there is nothing to walk to.
        /// </summary>
        internal static void BakeEverything()
        {
            int asked = 0;

            foreach (Luggage chest in Luggage.ALL_LUGGAGE)
            {
                if (chest == null || chest.IsOpen) continue;
                if (Ask(chest.gameObject)) asked++;
            }

            foreach (Capybara animal in FindObjectsOfType<Capybara>())
                if (Ask(animal.gameObject)) asked++;

            foreach (Scoutmaster monster in FindObjectsOfType<Scoutmaster>())
                if (Ask(monster.gameObject)) asked++;

            foreach (Mob mob in FindObjectsOfType<Mob>())
                if (Ask(mob.gameObject)) asked++;

            // Landmarks are known by name rather than by component, so they can
            // only be found by walking the level. Expensive, and paid once by
            // somebody who pressed a key on purpose.
            MapHandler map = Singleton<MapHandler>.Instance;

            if (map?.segments != null)
            {
                foreach (var segment in map.segments)
                {
                    if (segment?.segmentParent == null) continue;
                    asked += Sweep(segment.segmentParent.transform);
                }
            }

            Plugin.Logger.LogInfo(
                $"Minimap: found {asked} thing(s) worth an icon across the whole level, " +
                $"{IconBaker.Pending} kind(s) of them new.");
        }

        private static int Sweep(Transform branch)
        {
            // Stop at the first thing that matches. A belltower contains a
            // bell, and both answer to the classifier, so carrying on down
            // photographed one object twice under two of its own names — the
            // second one identical to the first, down to the pixel count, and
            // holding a second place in a cache of sixty-four.
            if (TryClassify(branch.gameObject, out Color _, out bool _))
                return Ask(branch.gameObject) ? 1 : 0;

            int asked = 0;

            for (int i = 0; i < branch.childCount; i++)
                asked += Sweep(branch.GetChild(i));

            return asked;
        }

        private static bool Ask(GameObject go)
        {
            string key = IconBaker.KeyFor(go);
            if (string.IsNullOrEmpty(key) || IconBaker.Get(key) != null) return false;

            IconBaker.Request(key, go);
            return true;
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
