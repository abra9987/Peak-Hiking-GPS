using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PeakMapInteractive.Minimap
{
    /// <summary>
    /// Turns a thing standing on the mountain into a small picture of itself,
    /// for use as a map marker.
    ///
    /// Markers used to be coloured dots. A dot says which category something
    /// belongs to and never says what it is, which is the difference between
    /// "there is loot over there" and "that is a suitcase, and I know how long
    /// one takes to open".
    ///
    /// The reference mod solves this by shipping PNGs pulled out of the game.
    /// This one must not: a Nexus release depends on carrying none of PEAK's
    /// artwork. So the picture is made on the player's own machine, from the
    /// model already sitting in the loaded scene.
    ///
    /// Items would have been easy — <c>Item.UIData.GetIcon()</c> hands over a
    /// texture, which is how the compass in the corner is drawn. Chests are the
    /// reason this file exists: <c>Luggage</c> derives from <c>Spawner</c>, not
    /// <c>Item</c>, so there is no icon anywhere in the game to borrow. What
    /// there is, is a model — so the model gets photographed.
    ///
    /// One photograph per kind of thing, taken the first time one is drawn on
    /// the map, kept for the rest of the run.
    /// </summary>
    internal static class IconBaker
    {
        private const int Resolution = 128;

        /// <summary>
        /// A ceiling on how many distinct icons are kept, in case a map turns
        /// out to have far more named variants than expected. Each one is a
        /// small texture, but a cache with no bound is still a leak.
        /// </summary>
        private const int MaxIcons = 64;

        /// <summary>
        /// Where the copy is stood up to be photographed: far below the world,
        /// clear of the water box at y = -1, which is 1000 units tall and so
        /// reaches down to -501.
        /// </summary>
        private static readonly Vector3 Stand = new Vector3(0f, -9000f, 0f);

        /// <summary>Baked icons, plus a null for anything that could not be baked.</summary>
        private static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        private static readonly Queue<Order> _queue = new Queue<Order>();
        private static readonly HashSet<string> _ordered = new HashSet<string>();
        private static bool _working;
        private static int _layer = -1;

        private struct Order
        {
            public string Key;
            public GameObject Source;
        }

        /// <summary>
        /// The icon for a kind of thing, or null while it is still being baked
        /// and for anything that never could be. Callers fall back to the plain
        /// marker, so the map is never waiting on this.
        /// </summary>
        internal static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            _icons.TryGetValue(key, out Sprite sprite);
            return sprite;
        }

        /// <summary>
        /// Asks for an icon to be baked from this object, if one is not already
        /// known. Cheap to call every frame: everything after the first request
        /// for a given key is a dictionary lookup.
        /// </summary>
        internal static void Request(string key, GameObject source)
        {
            if (string.IsNullOrEmpty(key) || source == null) return;
            if (_icons.ContainsKey(key) || _ordered.Contains(key)) return;
            if (_icons.Count >= MaxIcons) return;

            _ordered.Add(key);
            _queue.Enqueue(new Order { Key = key, Source = source });

            if (_working) return;

            _working = true;
            Plugin.Run(Work());
        }

        /// <summary>
        /// A stable name for the kind of thing an object is, so that every
        /// suitcase on the mountain shares one icon.
        ///
        /// Scene copies arrive as "Luggage_Suitcase_A (Clone) 3" and similar,
        /// so the clone suffix and any trailing copy number come off. Letters
        /// are left alone: "Luggage_A" and "Luggage_B" really are different
        /// models and deserve different pictures.
        /// </summary>
        internal static string KeyFor(GameObject go)
        {
            if (go == null) return null;

            string name = go.name;

            int clone = name.IndexOf("(Clone)", System.StringComparison.OrdinalIgnoreCase);
            if (clone >= 0) name = name.Substring(0, clone);

            int cut = name.Length;
            while (cut > 0 && IsCopySuffix(name[cut - 1])) cut--;
            if (cut > 2) name = name.Substring(0, cut);

            return name.Trim().ToLowerInvariant();
        }

        private static bool IsCopySuffix(char c)
            => char.IsDigit(c) || c == ' ' || c == '(' || c == ')' || c == '_' || c == '.';

        // --- baking ----------------------------------------------------------

        private static IEnumerator Work()
        {
            while (_queue.Count > 0)
            {
                Order order = _queue.Dequeue();
                yield return Bake(order);
            }

            _working = false;
        }

        private static IEnumerator Bake(Order order)
        {
            // Between the request and now the object may have been opened,
            // killed or unloaded. Record the failure so it is not retried on
            // every sighting for the rest of the run.
            if (order.Source == null)
            {
                _icons[order.Key] = null;
                yield break;
            }

            var temporary = new List<Mesh>();
            GameObject stand = BuildStand(order.Source, temporary, out Bounds bounds);

            if (stand == null)
            {
                _icons[order.Key] = null;
                yield break;
            }

            var target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "PeakMapInteractive_IconBake",
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();

            GameObject rig = BuildRig(bounds, target);

            // Camera.Render() does nothing at all under URP — silently, which
            // cost the exporter half a day. The only thing that works is
            // leaving an enabled camera for the pipeline to draw on its own,
            // so the result is not there until a frame has actually gone by.
            Sprite icon = null;

            for (int attempt = 0; attempt < 2 && icon == null; attempt++)
            {
                yield return new WaitForEndOfFrame();
                icon = Capture(target);
            }

            UnityEngine.Object.Destroy(rig);
            UnityEngine.Object.Destroy(stand);

            foreach (Mesh mesh in temporary)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);

            target.Release();
            UnityEngine.Object.Destroy(target);

            _icons[order.Key] = icon;

            if (icon == null)
            {
                Plugin.Logger.LogWarning($"Minimap: nothing could be baked for '{order.Key}'.");
                yield break;
            }

            Plugin.Logger.LogInfo(
                $"Minimap: baked icon for '{order.Key}' " +
                $"({(int)icon.rect.width}x{(int)icon.rect.height}).");

            if (Plugin.Settings.MinimapDumpIcons.Value) Dump(order.Key, icon);
        }

        /// <summary>
        /// Writes a baked icon out as a PNG, so it can be looked at properly.
        /// Off by default: this exists for judging the icons while they are
        /// being dialled in, not for the finished mod.
        /// </summary>
        private static void Dump(string key, Sprite icon)
        {
            try
            {
                string folder = System.IO.Path.Combine(Plugin.OutputDir, "icons");
                System.IO.Directory.CreateDirectory(folder);

                var safe = new System.Text.StringBuilder(key.Length);
                foreach (char c in key)
                    safe.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

                string path = System.IO.Path.Combine(folder, safe + ".png");
                System.IO.File.WriteAllBytes(path, icon.texture.EncodeToPNG());

                Plugin.Logger.LogInfo($"Minimap: wrote {path}");
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Minimap: could not write the icon for '{key}': {error.Message}");
            }
        }

        /// <summary>
        /// Builds a render-only copy of the object out of bare meshes.
        ///
        /// Instantiating the object itself would have been one line and a bad
        /// idea: a copied <c>Luggage</c> wakes up, registers itself in
        /// <c>ALL_LUGGAGE</c> and brings a <c>PhotonView</c> with it, so
        /// photographing a chest would quietly edit the run. Nothing here has
        /// any behaviour attached — meshes, materials, transforms, and that is
        /// all.
        /// </summary>
        private static GameObject BuildStand(GameObject source, List<Mesh> temporary, out Bounds bounds)
        {
            bounds = default;

            var stand = new GameObject("PeakMapInteractive_IconStand");
            stand.transform.SetPositionAndRotation(Stand, Quaternion.identity);

            Transform origin = source.transform;
            int layer = StandLayer();
            bool any = false;

            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!renderer.gameObject.activeInHierarchy) continue;

                Mesh mesh = MeshOf(renderer, temporary);
                if (mesh == null) continue;

                var piece = new GameObject(renderer.name);
                piece.layer = layer;
                piece.transform.SetParent(stand.transform, worldPositionStays: false);

                Transform from = renderer.transform;
                piece.transform.localPosition = origin.InverseTransformPoint(from.position);
                piece.transform.localRotation = Quaternion.Inverse(origin.rotation) * from.rotation;

                // A skinned mesh is baked with its own scale already applied,
                // so scaling it again would square it.
                piece.transform.localScale = renderer is SkinnedMeshRenderer
                    ? Vector3.one
                    : Relative(from.lossyScale, origin.lossyScale);

                piece.AddComponent<MeshFilter>().sharedMesh = mesh;

                var copy = piece.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.receiveShadows = false;
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                bounds = any ? Grow(bounds, copy.bounds) : copy.bounds;
                any = true;
            }

            if (!any)
            {
                UnityEngine.Object.Destroy(stand);
                return null;
            }

            return stand;
        }

        /// <summary>
        /// The mesh a renderer is currently drawing, posed. Anything that is
        /// not geometry — particles, trails, billboards — has no silhouette
        /// worth photographing and is skipped.
        /// </summary>
        private static Mesh MeshOf(Renderer renderer, List<Mesh> temporary)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                var posed = new Mesh { name = "PeakMapInteractive_IconPose" };
                skinned.BakeMesh(posed, useScale: true);
                temporary.Add(posed);
                return posed;
            }

            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            return null;
        }

        private static Vector3 Relative(Vector3 scale, Vector3 origin)
            => new Vector3(
                origin.x == 0f ? 1f : scale.x / origin.x,
                origin.y == 0f ? 1f : scale.y / origin.y,
                origin.z == 0f ? 1f : scale.z / origin.z);

        private static Bounds Grow(Bounds bounds, Bounds other)
        {
            bounds.Encapsulate(other);
            return bounds;
        }

        /// <summary>
        /// A layer nothing else uses, so the bake camera can be told to draw
        /// this and nothing else, and the player's camera never sees it.
        /// PEAK leaves plenty free: it names 0, 4, 10, 20, 21, 22, 29 and 31.
        /// </summary>
        private static int StandLayer()
        {
            if (_layer >= 0) return _layer;

            for (int layer = 31; layer >= 8; layer--)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;

                _layer = layer;
                return _layer;
            }

            // Nothing free. Default still works, because the only thing nine
            // kilometres under the map is what was just put there.
            _layer = 0;
            return _layer;
        }

        /// <summary>
        /// Camera and lights, framed on the copy.
        ///
        /// The view is along whichever horizontal axis the object is thinnest,
        /// so its widest silhouette faces the lens: a capybara photographed
        /// end-on is a brown blob, and from the side it is unmistakably a
        /// capybara. A little yaw and downward tilt on top of that stop it
        /// reading as a flat elevation drawing.
        /// </summary>
        private static GameObject BuildRig(Bounds bounds, RenderTexture target)
        {
            Vector3 size = bounds.size;
            float yaw = (size.x <= size.z ? 90f : 0f) + 20f;
            Quaternion rotation = Quaternion.Euler(24f, yaw, 0f);

            float half = HalfFrame(bounds.extents, rotation);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            float back = radius * 4f + 2f;

            var rig = new GameObject("PeakMapInteractive_IconRig");
            rig.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * back, rotation);

            var camera = rig.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = half;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << StandLayer();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = back + radius * 4f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depth = -100f;
            camera.targetTexture = target;

            // Post-processing would tone-map the transparent background into
            // something that is no longer transparent, and grade the icon along
            // with it. An icon wants the material's own colours, unmediated.
            try
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderType = CameraRenderType.Base;
                    data.renderPostProcessing = false;
                    data.renderShadows = false;
                    data.antialiasing = AntialiasingMode.None;
                    data.requiresColorOption = CameraOverrideOption.Off;
                    data.requiresDepthOption = CameraOverrideOption.Off;
                }
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Minimap: could not configure the bake camera: {error.Message}");
            }

            // The rig brings its own light, and they are point lights on
            // purpose. PEAK has a night: a chest photographed by the sun at
            // 3 a.m. would come out a black shape, and it would stay black for
            // the rest of the run because the icon is baked once. A point light
            // reaches only as far as its range, so nine kilometres up the
            // mountain nothing notices these exist.
            AddLight(rig.transform, bounds.center, rotation * new Vector3(-0.7f, 1f, -1.2f) * radius * 2f, 2.2f);
            AddLight(rig.transform, bounds.center, rotation * new Vector3(1f, 0.1f, -0.9f) * radius * 2.5f, 0.8f);

            return rig;
        }

        /// <summary>
        /// Half the frame the object needs, projected onto the camera's own
        /// axes. Using the bounding sphere instead would be safe and would
        /// leave a long object floating in the middle of a mostly empty icon.
        /// </summary>
        private static float HalfFrame(Vector3 extents, Quaternion rotation)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            float across = Mathf.Abs(extents.x * right.x) + Mathf.Abs(extents.y * right.y) + Mathf.Abs(extents.z * right.z);
            float tall = Mathf.Abs(extents.x * up.x) + Mathf.Abs(extents.y * up.y) + Mathf.Abs(extents.z * up.z);

            return Mathf.Max(across, tall, 0.05f) * 1.08f;
        }

        /// <summary>
        /// A point light a given offset away from the subject, bright enough to
        /// light it to <paramref name="brightness"/> once it gets there.
        ///
        /// The intensity has to be worked out from the distance rather than
        /// picked, because the offset is proportional to the size of the thing
        /// being photographed and a point light falls off with the square of
        /// the distance. A fixed intensity lights a suitcase properly and
        /// leaves a belltower almost black.
        /// </summary>
        private static void AddLight(Transform parent, Vector3 subject, Vector3 offset, float brightness)
        {
            float distanceSquared = Mathf.Max(offset.sqrMagnitude, 0.01f);

            var holder = new GameObject("Light");
            holder.transform.SetParent(parent, worldPositionStays: false);
            holder.transform.position = subject + offset;

            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.intensity = brightness * distanceSquared;

            // Comfortably past the subject, so the smooth cut-off URP applies
            // near the end of the range never reaches it.
            light.range = Mathf.Sqrt(distanceSquared) * 4f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // --- readback --------------------------------------------------------

        /// <summary>
        /// Reads the render back and trims it to what was actually drawn.
        ///
        /// Returns null when the frame came back empty, which is the signal to
        /// wait one more frame: the camera is built and read within the same
        /// tick, and the pipeline draws it somewhere in between.
        /// </summary>
        private static Sprite Capture(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;

            var full = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0f, 0f, Resolution, Resolution), 0, 0);
            full.Apply();

            RenderTexture.active = previous;

            Color32[] pixels = full.GetPixels32();
            UnityEngine.Object.Destroy(full);

            if (!TryFindContent(pixels, out int left, out int bottom, out int width, out int height))
            {
                // Either nothing was drawn, or it was drawn by a shader that
                // does not bother writing alpha for an opaque surface — and
                // from the pixels alone those look identical. Keying against
                // the background settles it, at the price of losing anything on
                // the model that is genuinely black.
                if (!TryKeyBackground(pixels)) return null;
                if (!TryFindContent(pixels, out left, out bottom, out width, out height)) return null;

                Plugin.Logger.LogInfo("Minimap: the icon shader wrote no alpha; keyed against the background instead.");
            }

            var trimmed = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true)
            {
                name = "PeakMapInteractive_Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var region = new Color32[width * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    region[y * width + x] = pixels[(bottom + y) * Resolution + left + x];

            trimmed.SetPixels32(region);

            // Mipmaps matter here more than usual: a 128-pixel photograph is
            // drawn at about twenty on the map, and without them that much
            // minification turns detail into sparkle.
            trimmed.Apply(updateMipmaps: true, makeNoLongerReadable: false);

            // FullRect rather than a tight mesh: a tight mesh is generated from
            // the alpha, and the marker draws a dark rim just outside it.
            return Sprite.Create(
                trimmed, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Rebuilds the alpha channel from what is not the background.
        ///
        /// The camera clears to transparent black, so anything with colour in
        /// it was drawn. Reports whether it found enough to be worth using: a
        /// frame that is entirely background really was empty.
        /// </summary>
        private static bool TryKeyBackground(Color32[] pixels)
        {
            const byte threshold = 8;
            int drawn = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                bool lit = pixel.r > threshold || pixel.g > threshold || pixel.b > threshold;

                pixels[i] = new Color32(pixel.r, pixel.g, pixel.b, lit ? (byte)255 : (byte)0);
                if (lit) drawn++;
            }

            return drawn > 16;
        }

        /// <summary>
        /// The box the object actually occupies, with a little air around it.
        ///
        /// Trimming is what gives every icon the same visual weight. Without
        /// it, a tall thin object is a sliver in the middle of the marker while
        /// a squat one fills the whole thing.
        /// </summary>
        private static bool TryFindContent(Color32[] pixels, out int left, out int bottom, out int width, out int height)
        {
            const byte threshold = 16;
            const int margin = 2;

            int minX = Resolution, minY = Resolution, maxX = -1, maxY = -1;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    if (pixels[y * Resolution + x].a < threshold) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
            {
                left = bottom = width = height = 0;
                return false;
            }

            left = Mathf.Max(minX - margin, 0);
            bottom = Mathf.Max(minY - margin, 0);
            width = Mathf.Min(maxX + margin, Resolution - 1) - left + 1;
            height = Mathf.Min(maxY + margin, Resolution - 1) - bottom + 1;

            return width > 1 && height > 1;
        }
    }
}
