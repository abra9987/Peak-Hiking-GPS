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
        /// <summary>
        /// A ceiling on how many distinct icons are kept, in case a map turns
        /// out to have far more named variants than expected. Each one is a
        /// small texture, but a cache with no bound is still a leak.
        /// </summary>
        private const int MaxIcons = 64;

        /// <summary>
        /// How far from a marker a renderer may sit and still count as part of
        /// the thing. Generous enough for a belltower, short enough to leave a
        /// beach behind.
        /// </summary>
        private const float Reach = 12f;

        /// <summary>
        /// The brightness the exposure loop aims the photograph at, and the
        /// range it will settle for. Both ends matter: below the floor a
        /// suitcase is a dark smudge, and above the ceiling it is a white one
        /// with the straps burned off — and once a pixel has clipped to white
        /// there is nothing left to recover afterwards.
        /// </summary>
        private const float Target = 150f;
        private const float Floor = 90f;
        private const float Ceiling = 205f;

        /// <summary>
        /// The layer the copy is drawn on. Default, deliberately.
        ///
        /// A private unused layer looks like the careful choice and is not:
        /// URP's renderer carries its own opaque and transparent layer masks,
        /// and whatever they leave out is silently never drawn. Default is the
        /// one layer certain to be rendered. Nothing can stray into the shot
        /// anyway: the camera is orthographic, a few metres deep, and nine
        /// kilometres beneath the map.
        /// </summary>
        private const int StandLayerIndex = 0;

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

        /// <summary>Side of the frame being photographed, read from config per bake.</summary>
        private static int _resolution = 128;

        private struct Order
        {
            public string Key;
            public GameObject Source;
        }

        /// <summary>Camera, lights and their original strengths, as one thing.</summary>
        private sealed class Rig
        {
            public GameObject Root;
            public Camera Camera;
            public Light[] Lights;
            public float[] Strength;
        }

        /// <summary>What one photograph came back with.</summary>
        private struct Shot
        {
            public Color32[] Pixels;
            public int Drawn;
            public float Average;
            public Color32[] OnBlack;
            public Color32[] OnWhite;
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
        /// are left alone: "LuggageBig" and "LuggageEpic" really are different
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

            _resolution = Mathf.Clamp(Plugin.Settings.MinimapIconResolution.Value, 64, 1024);

            var temporary = new List<Mesh>();
            GameObject stand = BuildStand(order.Source, temporary, out Bounds bounds);

            if (stand == null)
            {
                _icons[order.Key] = null;
                yield break;
            }

            var target = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "PeakMapInteractive_IconBake",
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();

            Rig rig = BuildRig(bounds, target);

            Plugin.Logger.LogInfo(
                $"Minimap: baking '{order.Key}' at {_resolution}px — bounds centre {bounds.center}, " +
                $"size {bounds.size}, half-frame {rig.Camera.orthographicSize:0.###}, " +
                $"lighting {Lighting()}");

            // Photographed until it comes out at a readable brightness, rather
            // than at one exposure picked in advance. The models arrive with
            // wildly different albedo — a scuffed brown suitcase and a white
            // stone statue under identical light are a dark smudge and a blown
            // white one — and the icon is baked once and kept, so there is no
            // second chance later.
            var shot = default(Shot);
            float exposure = 1f;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Expose(rig, exposure);

                // A whole frame each time, not WaitForEndOfFrame: a coroutine
                // already running in the end-of-frame phase resumes inside that
                // same phase, so back-to-back bakes read the texture twice with
                // no render in between and saw two identical frames. Resuming
                // in Update means the texture always holds the frame that has
                // just finished.
                rig.Camera.backgroundColor = Color.black;
                yield return null;
                yield return null;
                Color32[] onBlack = ReadBack(target);

                rig.Camera.backgroundColor = Color.white;
                yield return null;
                yield return null;
                Color32[] onWhite = ReadBack(target);

                shot = Compose(onBlack, onWhite);

                if (shot.Drawn == 0 || shot.Drawn == shot.Pixels.Length) break;
                if (shot.Average >= Floor && shot.Average <= Ceiling) break;

                // Output is gamma-encoded, so brightness moves roughly as the
                // 1/2.2 power of the light: correcting in one step needs the
                // ratio raised back by that much.
                exposure *= Mathf.Pow(Target / Mathf.Max(shot.Average, 1f), 2.2f);
                exposure = Mathf.Clamp(exposure, 0.002f, 50f);

                Plugin.Logger.LogInfo(
                    $"Minimap: '{order.Key}' came out at {shot.Average:0.#} on attempt {attempt}; " +
                    $"re-lighting at {exposure:0.###}x.");
            }

            Sprite icon = Finish(order.Key, shot);

            UnityEngine.Object.Destroy(rig.Root);
            UnityEngine.Object.Destroy(stand);

            foreach (Mesh mesh in temporary)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);

            target.Release();
            UnityEngine.Object.Destroy(target);

            _icons[order.Key] = icon;

            if (icon == null) yield break;

            Plugin.Logger.LogInfo(
                $"Minimap: baked icon for '{order.Key}' " +
                $"({(int)icon.rect.width}x{(int)icon.rect.height}).");

            if (Plugin.Settings.MinimapDumpIcons.Value) Dump(order.Key, icon);
        }

        /// <summary>
        /// Turns the last photograph into a sprite, or explains why it could
        /// not. Split out from the exposure loop so that everything the loop
        /// needs to decide is separate from everything done once at the end.
        /// </summary>
        private static Sprite Finish(string key, Shot shot)
        {
            if (shot.Pixels == null)
            {
                Plugin.Logger.LogWarning($"Minimap: '{key}' was never photographed.");
                return null;
            }

            if (Plugin.Settings.MinimapDumpIcons.Value)
            {
                DumpFrame(key + "_on_black", shot.OnBlack);
                DumpFrame(key + "_on_white", shot.OnWhite);
            }

            if (shot.Drawn == 0)
            {
                Plugin.Logger.LogWarning($"Minimap: '{key}' came back empty — nothing was drawn at all.");
                return null;
            }

            // Every single pixel opaque means the two frames never differed,
            // and two frames only fail to differ when the camera did not draw
            // them. A real icon cannot fill the frame — the framing leaves
            // eight per cent of air around the subject on purpose.
            if (shot.Drawn == shot.Pixels.Length)
            {
                Plugin.Logger.LogWarning(
                    $"Minimap: '{key}' filled the whole frame, so the two photographs were " +
                    "identical and the camera never rendered.");
                return null;
            }

            Plugin.Logger.LogInfo(
                $"Minimap: '{key}' covers {shot.Drawn} of {shot.Pixels.Length} pixels, " +
                $"averaging {shot.Average:0.#}.");

            Brighten(key, shot.Pixels, shot.Average);

            return Trim(shot.Pixels);
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
            bool any = false;
            int far = 0;

            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!renderer.gameObject.activeInHierarchy) continue;

                // Landmarks are recognised by name, and a name sits on whatever
                // object the level designer put it on. "Beach_Campfire" turned
                // out to be 360 renderers spread over 356 metres — a campfire
                // and the entire beach around it. Anything that far from the
                // marker is scenery, not the thing being photographed.
                if (Vector3.Distance(renderer.transform.position, origin.position) > Reach)
                {
                    far++;
                    continue;
                }

                Mesh mesh = MeshOf(renderer, temporary);
                if (mesh == null) continue;

                var piece = new GameObject(renderer.name);
                piece.layer = StandLayerIndex;
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

                // Worked out from the mesh rather than read off the renderer.
                // Renderer.bounds on something created this same frame is not
                // reliably filled in yet, and empty bounds would put the camera
                // at the world origin looking at nothing.
                Bounds part = MeshBounds(piece.transform, mesh);
                bounds = any ? Grow(bounds, part) : part;
                any = true;
            }

            if (!any)
            {
                UnityEngine.Object.Destroy(stand);
                return null;
            }

            Plugin.Logger.LogInfo(
                $"Minimap: '{source.name}' copied as {stand.transform.childCount} renderer(s)" +
                (far > 0 ? $", {far} left behind as scenery." : "."));

            return stand;
        }

        /// <summary>
        /// A mesh's bounding box in world space, by transforming all eight
        /// corners rather than the box itself: rotating a box and taking the
        /// result's extents shrinks it.
        /// </summary>
        private static Bounds MeshBounds(Transform piece, Mesh mesh)
        {
            Bounds local = mesh.bounds;
            Matrix4x4 matrix = piece.localToWorldMatrix;
            Vector3 centre = local.center;
            Vector3 extents = local.extents;

            var bounds = new Bounds(matrix.MultiplyPoint3x4(centre - extents), Vector3.zero);

            for (int corner = 1; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);

                bounds.Encapsulate(matrix.MultiplyPoint3x4(centre + offset));
            }

            return bounds;
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
        /// What the renderer is set up to do with lights, for the log. If
        /// additional lights are off in the pipeline asset, the rig's own point
        /// lights do nothing and every icon comes out black.
        /// </summary>
        private static string Lighting()
        {
            try
            {
                var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    as UniversalRenderPipelineAsset;

                if (asset == null) return "not URP";

                return $"additional lights {asset.additionalLightsRenderingMode}, " +
                       $"ambient {RenderSettings.ambientMode}/{RenderSettings.ambientIntensity:0.##}";
            }
            catch (System.Exception error)
            {
                return "unknown (" + error.Message + ")";
            }
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
        private static Rig BuildRig(Bounds bounds, RenderTexture target)
        {
            Vector3 size = bounds.size;
            float yaw = (size.x <= size.z ? 90f : 0f) + 20f;
            Quaternion rotation = Quaternion.Euler(24f, yaw, 0f);

            float half = HalfFrame(bounds.extents, rotation);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            float back = radius * 4f + 2f;

            var root = new GameObject("PeakMapInteractive_IconRig");
            root.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * back, rotation);

            var camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = half;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << StandLayerIndex;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = back + radius * 4f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depth = -100f;
            camera.targetTexture = target;

            // Post-processing would tone-map the background into something that
            // is no longer the colour it was cleared to, which the whole
            // black-and-white keying depends on, and grade the icon along with
            // it. An icon wants the material's own colours, unmediated.
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
            var lights = new[]
            {
                AddLight(root.transform, bounds.center, rotation * new Vector3(-0.7f, 1f, -1.2f) * radius * 2f, 1f),
                AddLight(root.transform, bounds.center, rotation * new Vector3(1f, 0.1f, -0.9f) * radius * 2.5f, 0.35f)
            };

            var strength = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) strength[i] = lights[i].intensity;

            return new Rig { Root = root, Camera = camera, Lights = lights, Strength = strength };
        }

        /// <summary>Turns the rig's lights up or down together.</summary>
        private static void Expose(Rig rig, float exposure)
        {
            for (int i = 0; i < rig.Lights.Length; i++)
                rig.Lights[i].intensity = rig.Strength[i] * exposure;
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
        private static Light AddLight(Transform parent, Vector3 subject, Vector3 offset, float brightness)
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

            return light;
        }

        // --- readback --------------------------------------------------------

        private static Color32[] ReadBack(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;

            var full = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0f, 0f, _resolution, _resolution), 0, 0);
            full.Apply();

            RenderTexture.active = previous;

            Color32[] pixels = full.GetPixels32();
            UnityEngine.Object.Destroy(full);

            return pixels;
        }

        /// <summary>
        /// Builds the icon from the two photographs.
        ///
        /// Wherever the two frames agree, light passed through nothing and the
        /// object is there. Wherever black became white, that pixel is empty.
        /// The gap between the two is exactly how much of the pixel the
        /// background showed through, which is the alpha channel the pipeline
        /// would not give up: asked to clear to transparent, URP hands back a
        /// fully opaque texture regardless.
        /// </summary>
        private static Shot Compose(Color32[] onBlack, Color32[] onWhite)
        {
            var pixels = new Color32[onBlack.Length];
            int drawn = 0;
            long total = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 dark = onBlack[i];
                Color32 light = onWhite[i];

                int shown = Mathf.Max(
                    Mathf.Max(light.r - dark.r, light.g - dark.g),
                    light.b - dark.b);

                byte alpha = (byte)Mathf.Clamp(255 - shown, 0, 255);

                if (alpha == 0)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // The dark frame is the object's colour already multiplied by
                // its own coverage, so an edge pixel has to be divided back out
                // or the whole silhouette gets a dark fringe.
                float recover = 255f / alpha;
                pixels[i] = new Color32(
                    (byte)Mathf.Min(dark.r * recover, 255f),
                    (byte)Mathf.Min(dark.g * recover, 255f),
                    (byte)Mathf.Min(dark.b * recover, 255f),
                    alpha);

                if (alpha <= 128) continue;

                drawn++;
                total += (pixels[i].r + pixels[i].g + pixels[i].b) / 3;
            }

            return new Shot
            {
                Pixels = pixels,
                Drawn = drawn,
                Average = drawn == 0 ? 0f : (float)total / drawn,
                OnBlack = onBlack,
                OnWhite = onWhite
            };
        }

        /// <summary>
        /// Lifts an icon that is still dark after the exposure loop has done
        /// what it can — some models are simply dark, and a dark suitcase on a
        /// dark plate is a smudge.
        ///
        /// A gamma curve rather than a multiplier, so the bright parts stay put
        /// instead of clipping: a suitcase lit to an average of sixty keeps its
        /// clasps and its straps.
        /// </summary>
        private static void Brighten(string key, Color32[] pixels, float average)
        {
            if (average < 1f) average = 1f;

            float gamma = Mathf.Clamp(
                Mathf.Log(Target / 255f) / Mathf.Log(average / 255f), 0.35f, 1f);

            if (gamma > 0.995f) return;

            var curve = new byte[256];
            for (int value = 0; value < 256; value++)
                curve[value] = (byte)Mathf.Clamp(Mathf.Pow(value / 255f, gamma) * 255f, 0f, 255f);

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0) continue;

                pixels[i] = new Color32(
                    curve[pixels[i].r], curve[pixels[i].g], curve[pixels[i].b], pixels[i].a);
            }

            Plugin.Logger.LogInfo(
                $"Minimap: '{key}' lifted from {average:0.#} towards {Target:0} with gamma {gamma:0.###}.");
        }

        /// <summary>
        /// Cuts the icon down to what was actually drawn.
        ///
        /// Trimming is what gives every icon the same visual weight. Without
        /// it, a tall thin object is a sliver in the middle of the marker while
        /// a squat one fills the whole thing.
        /// </summary>
        private static Sprite Trim(Color32[] pixels)
        {
            if (!TryFindContent(pixels, out int left, out int bottom, out int width, out int height))
                return null;

            var trimmed = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true)
            {
                name = "PeakMapInteractive_Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var region = new Color32[width * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    region[y * width + x] = pixels[(bottom + y) * _resolution + left + x];

            trimmed.SetPixels32(region);

            // Mipmaps matter here more than usual: the photograph is several
            // hundred pixels across and drawn at about twenty on the map, and
            // without them that much minification turns detail into sparkle.
            trimmed.Apply(updateMipmaps: true, makeNoLongerReadable: false);

            // FullRect rather than a tight mesh: a tight mesh is generated from
            // the alpha, and the marker draws a dark rim just outside it.
            return Sprite.Create(
                trimmed, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
        }

        private static bool TryFindContent(Color32[] pixels, out int left, out int bottom, out int width, out int height)
        {
            const byte threshold = 16;
            int margin = Mathf.Max(_resolution / 64, 1);

            int minX = _resolution, minY = _resolution, maxX = -1, maxY = -1;

            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    if (pixels[y * _resolution + x].a < threshold) continue;

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
            width = Mathf.Min(maxX + margin, _resolution - 1) - left + 1;
            height = Mathf.Min(maxY + margin, _resolution - 1) - bottom + 1;

            return width > 1 && height > 1;
        }

        // --- looking at the results ------------------------------------------

        /// <summary>
        /// Writes a baked icon out as a PNG, so it can be looked at properly.
        /// Off by default: this exists for judging the icons while they are
        /// being dialled in, not for the finished mod.
        /// </summary>
        private static void Dump(string key, Sprite icon) => Write(key, icon.texture.EncodeToPNG());

        /// <summary>
        /// Writes one of the two raw frames out, exactly as the camera saw it.
        /// When an icon comes out wrong this is the picture that says which
        /// half went wrong — nothing drawn, or drawn and unlit.
        /// </summary>
        private static void DumpFrame(string name, Color32[] pixels)
        {
            if (pixels == null) return;

            var frame = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            frame.SetPixels32(pixels);
            frame.Apply();

            Write(name, frame.EncodeToPNG());
            UnityEngine.Object.Destroy(frame);
        }

        private static void Write(string key, byte[] png)
        {
            try
            {
                string folder = System.IO.Path.Combine(Plugin.OutputDir, "icons");
                System.IO.Directory.CreateDirectory(folder);

                var safe = new System.Text.StringBuilder(key.Length);
                foreach (char c in key)
                    safe.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

                string path = System.IO.Path.Combine(folder, safe + ".png");
                System.IO.File.WriteAllBytes(path, png);
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Minimap: could not write '{key}': {error.Message}");
            }
        }
    }
}
