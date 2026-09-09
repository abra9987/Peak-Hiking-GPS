using UnityEngine;
using UnityEngine.UI;

namespace PeakMapInteractive.Minimap
{
    /// <summary>
    /// A live top-down view of the mountain, in a small window inside the game.
    ///
    /// This is the map the project was actually after. Every problem that made
    /// an exported map fall short dissolves here: nothing has to be read out of
    /// meshes the engine will not hand over, nothing has to be shipped, and no
    /// shader has to be reproduced — the game draws its own world, so the view
    /// is identical to the game by construction rather than by effort.
    ///
    /// It is built for route planning: north stays up so directions do not
    /// swim while the player turns, and the camera can be lifted far above the
    /// player to see what is coming rather than only what is underfoot.
    /// </summary>
    internal sealed class MinimapController : MonoBehaviour
    {
        private const int Resolution = 512;

        private Camera _camera;
        private RenderTexture _target;
        private Canvas _canvas;
        private RawImage _image;
        private RectTransform _panel;

        private bool _visible = true;
        private float _span;

        private void Awake()
        {
            _span = Plugin.Settings.MinimapSpan.Value;
            BuildCamera();
            BuildOverlay();
            Apply();
        }

        private void BuildCamera()
        {
            var holder = new GameObject("PeakMapInteractive_MinimapCamera");
            holder.transform.SetParent(transform, worldPositionStays: false);

            _camera = holder.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            // Below every game camera, so it can never composite over the view.
            _camera.depth = -50f;
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
        /// Ground and structures only. Weather, effects and the character
        /// itself would sit between the camera and the terrain, and a map you
        /// cannot see the ground through is not a map.
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
            // Above the game's HUD, which is what makes it a picture-in-picture
            // rather than something hidden behind the interface.
            _canvas.sortingOrder = 500;
            canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);

            _panel = panelObject.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(1f, 1f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-16f, -16f);

            float size = Plugin.Settings.MinimapSize.Value;
            _panel.sizeDelta = new Vector2(size, size);

            _image = panelObject.AddComponent<RawImage>();
            _image.texture = _target;
            _image.raycastTarget = false;

            AddPlayerMarker(panelObject.transform);
        }

        /// <summary>
        /// The player sits dead centre because the camera follows them, so the
        /// marker is a fixed dot rather than anything that needs projecting.
        /// </summary>
        private static void AddPlayerMarker(Transform parent)
        {
            var markerObject = new GameObject("You");
            markerObject.transform.SetParent(parent, worldPositionStays: false);

            var rect = markerObject.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(9f, 9f);

            var image = markerObject.AddComponent<Image>();
            image.color = new Color(1f, 0.55f, 0.2f, 1f);
            image.raycastTarget = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.Settings.MinimapToggleKey.Value))
            {
                _visible = !_visible;
                Apply();
            }

            if (!_visible) return;

            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomInKey.Value)) SetSpan(_span * 0.7f);
            if (Input.GetKeyDown(Plugin.Settings.MinimapZoomOutKey.Value)) SetSpan(_span / 0.7f);

            Follow();
        }

        private void SetSpan(float span)
        {
            _span = Mathf.Clamp(span, 20f, 2000f);
            if (_camera != null) _camera.orthographicSize = _span * 0.5f;
        }

        /// <summary>
        /// Keeps the camera directly above the player, high enough to see over
        /// the terrain, with north fixed upward.
        /// </summary>
        private void Follow()
        {
            Character player = Character.localCharacter;
            if (player == null || _camera == null) return;

            Vector3 position = player.Center;

            _camera.transform.position = new Vector3(position.x, position.y + 600f, position.z);
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.orthographicSize = _span * 0.5f;
            _camera.nearClipPlane = 1f;
            // Deep enough to see the ground far below when hanging off a cliff.
            _camera.farClipPlane = 3000f;
        }

        private void Apply()
        {
            if (_camera != null) _camera.enabled = _visible;
            if (_canvas != null) _canvas.enabled = _visible;
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
