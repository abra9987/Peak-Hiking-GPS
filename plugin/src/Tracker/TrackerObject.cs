using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// Turns the loaded model into an object standing in the world.
    ///
    /// The parts arrive from <see cref="TrackerModel"/> already in Unity's
    /// coordinates, so nothing here rotates or scales anything: a part becomes
    /// a child at its own offset and that is the whole of the arithmetic. What
    /// this file is actually for is the two things the blob cannot carry —
    /// which shader draws it, and which mesh is collision rather than surface.
    /// </summary>
    internal static class TrackerObject
    {
        /// <summary>
        /// The mesh that is the item's shape to physics, and never drawn.
        ///
        /// Blender can mark a mesh as collision-only; neither FBX nor glTF
        /// carries that as anything Unity restores, so it arrives as an
        /// ordinary renderable box around the device. The name is the only
        /// thing that survives, so the name is what is used.
        /// </summary>
        private const string ColliderPart = "Tracker_Collider";

        /// <summary>The face the map is drawn on.</summary>
        internal const string ScreenPart = "Tracker_Screen";

        /// <summary>
        /// The two materials a device needs, kept per shader.
        ///
        /// Shared, so one device or twenty is still two materials — and keyed,
        /// because which shader to draw with is not yet settled and the way to
        /// settle it is to stand both versions side by side and look.
        /// </summary>
        private sealed class Kit
        {
            internal Material Body;
            internal Material Screen;
        }

        private static readonly Dictionary<string, Kit> _kits = new Dictionary<string, Kit>();

        /// <summary>
        /// Builds the device. Returns null if the model did not load, rather
        /// than an empty object that looks like a different bug.
        /// </summary>
        internal static GameObject Build(string name = "HikingGPS_Tracker", string shaderName = null)
        {
            if (!TrackerModel.Available)
            {
                Plugin.Logger.LogWarning("Tracker: asked to build the device, but the model is not loaded.");
                return null;
            }

            Kit kit = KitFor(shaderName);
            var root = new GameObject(name);

            foreach (TrackerModel.Part part in TrackerModel.Parts)
            {
                var child = new GameObject(part.Name);
                child.transform.SetParent(root.transform, worldPositionStays: false);
                child.transform.localPosition = part.Offset;

                if (part.Name == ColliderPart)
                {
                    // Convex, because a held item is moved by physics and Unity
                    // will not let a non-convex mesh collider be anything but
                    // static scenery.
                    var collider = child.AddComponent<MeshCollider>();
                    collider.sharedMesh = RestlessHull(part.Mesh.bounds);
                    collider.convex = true;
                    continue;
                }

                child.AddComponent<MeshFilter>().sharedMesh = part.Mesh;

                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = part.Name == ScreenPart ? kit.Screen : kit.Body;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            root.AddComponent<TrackerDevice>();
            return root;
        }

        /// <summary>
        /// A collision shape with only one way to lie still: on its back.
        ///
        /// The case's own box stood on its bottom edge when dropped upright,
        /// the way a book stands on a shelf, and lay on its glass as happily
        /// as on its back. Weighting it could not fix that — a few centimetres
        /// of centre-of-mass bias decide which way a thing topples only if it
        /// topples at all. So the hull is shaped instead, in the model's own
        /// frame where +Z is the screen:
        ///
        /// - The back face is inset on every side, so every side face leans.
        ///   Upright, the lowest line is the front-bottom edge, and the centre
        ///   of mass sits behind it: it falls over backwards, and cannot lean
        ///   back onto the sloping bottom either, because that face is too
        ///   short to catch it. The same on its head or on either side.
        /// - The front carries a low ridge, set off centre, so it cannot lie
        ///   on its glass: it rocks off the ridge onto a side edge, and the
        ///   side edge tips it onto its back.
        ///
        /// None of it is visible. The ridge stands a few millimetres proud of
        /// the glass and the inset is hidden inside the case; nothing here
        /// changes how it is picked up or held.
        /// </summary>
        private static Mesh RestlessHull(Bounds b)
        {
            Vector3 lo = b.min, hi = b.max;
            float w = hi.x - lo.x, h = hi.y - lo.y, d = hi.z - lo.z;
            float inset = 0.12f * Mathf.Min(w, h);
            float ridge = 0.25f * d;
            float ridgeX = lo.x + 0.62f * w;

            var vertices = new[]
            {
                // The front, full size, at the glass.
                new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z),
                new Vector3(hi.x, hi.y, hi.z), new Vector3(lo.x, hi.y, hi.z),
                // The ridge down the front, off centre.
                new Vector3(ridgeX, lo.y, hi.z + ridge), new Vector3(ridgeX, hi.y, hi.z + ridge),
                // The back, drawn in on every side.
                new Vector3(lo.x + inset, lo.y + inset, lo.z), new Vector3(hi.x - inset, lo.y + inset, lo.z),
                new Vector3(hi.x - inset, hi.y - inset, lo.z), new Vector3(lo.x + inset, hi.y - inset, lo.z)
            };

            // Unity builds the convex hull itself from whatever triangles it
            // is given; these only need to touch every vertex.
            var triangles = new[]
            {
                0, 1, 2,  0, 2, 3,          // front
                0, 4, 1,  3, 2, 5,          // ridge
                6, 8, 7,  6, 9, 8,          // back
                0, 3, 9,  0, 9, 6,          // left
                1, 7, 8,  1, 8, 2,          // right
                0, 6, 7,  0, 7, 1,          // bottom
                3, 2, 8,  3, 8, 9           // top
            };

            var mesh = new Mesh { name = "Tracker_Collider_Restless" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // --- materials -------------------------------------------------------

        /// <summary>
        /// The pair of materials for one shader, made once and shared.
        /// </summary>
        private static Kit KitFor(string shaderName)
        {
            string key = shaderName ?? string.Empty;

            if (_kits.TryGetValue(key, out Kit cached)) return cached;

            Shader shader = shaderName == null ? Lit() : Shader.Find(shaderName);

            if (shader == null)
            {
                Plugin.Logger.LogWarning($"Tracker: '{shaderName}' is not in this build; using the usual one.");
                shader = Lit();
            }

            var kit = new Kit { Body = BuildBody(shader), Screen = BuildScreen(shader) };

            _kits[key] = kit;
            return kit;
        }

        /// <summary>
        /// The case: flat swatches out of a palette, lit by the mountain's own sun.
        /// </summary>
        private static Material BuildBody(Shader shader)
        {
            var material = new Material(shader) { name = "HikingGPS_Body" };

            Texture2D palette = TrackerModel.Texture("tracker_body_palette");
            if (palette != null) SetTexture(material, palette);

            // White underneath, or the palette is multiplied by whatever colour
            // the shader happened to start with.
            SetColour(material, Color.white);

            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_BaseMetallic", 0f);

            // Low, because the palette is the colour and a specular wash on top
            // of it is not a highlight but a loss. At 0.35 the case came out of
            // a midday beach as pale yellow instead of orange, and every dark
            // part as brown.
            SetFloat(material, "_Smoothness", 0.15f);
            SetFloat(material, "_Glossiness", 0.15f);
            SetFloat(material, "_BaseSmooth", 0.15f);
            SetFloat(material, "_AddSpecular", 0f);

            ApplyMask(material);

            return material;
        }

        /// <summary>
        /// Metal and smoothness per swatch, if this shader can read them.
        ///
        /// Without it every part of the case is the same plastic: the rubber
        /// grips, the antenna and the screen bezel all catch the light
        /// identically, which is the one thing that gives a modelled prop away
        /// as a modelled prop. PEAK's own shader cannot take it — it has a
        /// single figure for the whole material — so this does nothing there,
        /// and that is the trade being weighed.
        ///
        /// The keyword matters as much as the texture. Setting the map alone
        /// leaves the shader compiled without the branch that samples it, so it
        /// is assigned, held, and never read.
        /// </summary>
        private static void ApplyMask(Material material)
        {
            Texture2D mask = TrackerModel.Texture("tracker_body_mask");
            if (mask == null) return;

            if (material.HasProperty("_MetallicGlossMap"))
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");

                // Smoothness out of the mask's alpha rather than the albedo's.
                SetFloat(material, "_SmoothnessTextureChannel", 0f);

                // These become multipliers once a map is present, so anything
                // less than one quietly scales the whole mask down.
                SetFloat(material, "_Metallic", 1f);
                SetFloat(material, "_Smoothness", 1f);
                SetFloat(material, "_GlossMapScale", 1f);

                Plugin.Logger.LogInfo($"Tracker: '{material.shader.name}' is using the material mask.");
                return;
            }

            Plugin.Logger.LogInfo(
                $"Tracker: '{material.shader.name}' cannot take the mask; the case is one material throughout.");
        }

        /// <summary>
        /// The screen, before there is a map on it.
        ///
        /// Deliberately near-black rather than blank: an unlit screen is what
        /// the device looks like switched off, and starting from that makes it
        /// obvious later whether the map is actually being drawn or whether
        /// something is merely painting the panel white.
        /// </summary>
        private static Material BuildScreen(Shader shader)
        {
            // Unlit, not lit, and not the shader the case uses.
            //
            // A screen is backlit. Drawn with a lit shader it is a painted
            // panel: it goes dark at dusk, which is exactly when somebody most
            // wants to know where they are, and it catches the sky as a
            // reflection across the map. Unlit means the picture arrives at the
            // brightness it was drawn at, in any weather and at any hour, which
            // is what a display does.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit")
                           ?? Shader.Find("Unlit/Texture")
                           ?? shader;

            var material = new Material(unlit) { name = "HikingGPS_Screen" };

            // Off, until there is a map to put on it.
            SetColour(material, new Color(0.035f, 0.055f, 0.065f, 1f));
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_BaseMetallic", 0f);
            SetFloat(material, "_Smoothness", 0f);
            SetFloat(material, "_BaseSmooth", 0f);

            _screenMaterials.Add(material);

            // A screen built after the map was handed over still gets it. The
            // first version only walked the materials that existed at the time,
            // which during a preview run was none of them - and said so in the
            // log as though it had worked.
            if (_map != null)
            {
                SetTexture(material, _map);
                SetColour(material, Color.white);
            }

            return material;
        }

        private static readonly List<Material> _screenMaterials = new List<Material>();

        /// <summary>
        /// Puts the live map on every device's screen.
        ///
        /// One texture serves all of them: two people holding one each are
        /// looking at the same mountain from the same camera, so there is
        /// nothing to keep apart. Called whenever the map's screen texture
        /// appears or is rebuilt; harmless before then.
        /// </summary>
        internal static void ShowMap(Texture screen)
        {
            if (screen == null) return;

            _map = screen;
            int lit = 0;

            foreach (Material material in _screenMaterials)
            {
                if (material == null) continue;

                SetTexture(material, screen);
                SetColour(material, Color.white);
                lit++;
            }

            Plugin.Logger.LogInfo(
                $"Tracker: the map ({screen.width}x{screen.height}) is on {lit} screen(s); " +
                "any built later will take it too.");
        }

        private static Texture _map;

        // --- shaders ---------------------------------------------------------

        private static Shader _lit;

        /// <summary>
        /// A lit shader that is certainly in the game.
        ///
        /// Not shipped, found: a build strips every shader no material
        /// references, so the only ones that can be relied on are the ones the
        /// game itself draws with. Both <c>W/Peak_Standard</c> and the
        /// pipeline's stock <c>Lit</c> survive in this build; the game's own is
        /// asked for first, and that order was settled by looking rather than
        /// by argument.
        ///
        /// The two were built side by side and photographed together on the
        /// beach. PEAK's own shader draws a device that belongs in the game:
        /// orange case, dark grips, the colours of the artwork. The pipeline's
        /// own — which is the only one that can read the material mask —
        /// turned the case olive and the buttons and screen bezel deep blue,
        /// because a smoothness of 0.78 on a bezel makes it a mirror and what
        /// it had to reflect was a tropical sky and palm trees. The mask worked
        /// exactly as designed and the result looked like a prop from another
        /// game.
        ///
        /// So the mask is carried and not used, and that is the trade: one
        /// figure for metal and smoothness across the whole case, in exchange
        /// for a device that sits in PEAK's light the way PEAK's own things do.
        /// </summary>
        private static Shader Lit()
        {
            if (_lit != null) return _lit;

            foreach (string name in new[]
                     {
                         "W/Peak_Standard",
                         "Universal Render Pipeline/Lit",
                         "Universal Render Pipeline/Simple Lit",
                         "Standard"
                     })
            {
                _lit = Shader.Find(name);
                if (_lit == null) continue;

                Plugin.Logger.LogInfo($"Tracker: drawing with '{_lit.name}'.");
                Describe(_lit);
                return _lit;
            }

            _lit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            Plugin.Logger.LogWarning(
                $"Tracker: no lit shader was found; falling back to '{(_lit == null ? "nothing" : _lit.name)}'.");

            return _lit;
        }

        /// <summary>
        /// Which shaders this build actually kept, and whether any of them can
        /// take the material mask as a texture.
        ///
        /// The mask says metal in red and smoothness in alpha, one value per
        /// palette swatch, so that the rubber grips, the antenna and the screen
        /// bezel stop all being the same plastic. Only a shader with a
        /// metallic-gloss map can read it. PEAK's own has scalars instead —
        /// <c>_BaseMetallic</c> and <c>_BaseSmooth</c>, one figure for the whole
        /// case — so if nothing here offers a map, the mask has to be applied
        /// by splitting the case into one material per distinct pair, and this
        /// is the diagnostic that decides which.
        /// </summary>
        internal static void ProbeShaders()
        {
            foreach (string name in new[]
                     {
                         "W/Peak_Standard",
                         "Universal Render Pipeline/Lit",
                         "Universal Render Pipeline/Simple Lit",
                         "Standard"
                     })
            {
                Shader shader = Shader.Find(name);

                if (shader == null)
                {
                    Plugin.Logger.LogInfo($"Tracker: '{name}' is not in this build.");
                    continue;
                }

                bool map = false;

                foreach (string property in new[] { "_MetallicGlossMap", "_MaskMap", "_SpecGlossMap" })
                {
                    if (!HasProperty(shader, property)) continue;

                    Plugin.Logger.LogInfo($"Tracker: '{name}' takes a mask as '{property}'.");
                    map = true;
                }

                if (!map) Plugin.Logger.LogInfo($"Tracker: '{name}' has no mask texture slot.");

                Describe(shader);
            }
        }

        private static bool HasProperty(Shader shader, string name)
        {
            try
            {
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyName(i) == name) return true;
                }
            }
            catch
            {
                /* asked and it would not say */
            }

            return false;
        }

        /// <summary>
        /// Writes out what the chosen shader actually offers.
        ///
        /// Every property below is set by name, and a name that is wrong is
        /// silently ignored — a material that looks plausible and reacts to
        /// nothing. Since the shader is the game's and its properties are not
        /// documented anywhere, the cheapest way to learn them is to make the
        /// first unattended run say so.
        /// </summary>
        private static void Describe(Shader shader)
        {
            try
            {
                var line = new StringBuilder($"Tracker: '{shader.name}' declares");

                for (int i = 0; i < shader.GetPropertyCount(); i++)
                    line.Append($" {shader.GetPropertyName(i)}:{shader.GetPropertyType(i)}");

                Plugin.Logger.LogInfo(line.ToString());
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Tracker: could not read the shader's properties: {error.Message}");
            }
        }

        // --- setting properties without guessing -----------------------------

        /// <summary>
        /// The base texture, under whichever name this shader calls it.
        ///
        /// <c>Material.mainTexture</c> only works when the shader tags a
        /// property as the main one, which PEAK's own shaders do not. The first
        /// attempt at this asked for <c>_BaseMap</c> and <c>_MainTex</c>, and
        /// <c>W/Peak_Standard</c> has neither: the palette went nowhere, no
        /// warning was raised, and the device came out of the game white.
        /// The name it wants is <c>_BaseTexture</c>, and it also has a dial for
        /// how much of that texture to use, which starts at none.
        /// </summary>
        private static void SetTexture(Material material, Texture texture)
        {
            bool set = false;

            foreach (string name in new[] { "_BaseTexture", "_BaseMap", "_MainTex", "_BaseColorMap" })
            {
                if (!material.HasProperty(name)) continue;

                material.SetTexture(name, texture);
                set = true;
                break;
            }

            if (!set)
            {
                material.mainTexture = texture;
                Plugin.Logger.LogWarning(
                    $"Tracker: '{material.shader.name}' names its base texture something unexpected.");
            }

            SetFloat(material, "_BaseTexAmount", 1f);
        }

        private static void SetColour(Material material, Color colour)
        {
            bool set = false;

            foreach (string name in new[] { "_BaseColor", "_Color", "_TopColor", "_Tint" })
            {
                if (!material.HasProperty(name)) continue;

                material.SetColor(name, colour);
                set = true;
            }

            if (!set) material.color = colour;
        }

        private static void SetFloat(Material material, string name, float value)
        {
            if (material.HasProperty(name)) material.SetFloat(name, value);
        }

        /// <summary>Every renderer on a built device, for tinting or hiding it.</summary>
        internal static IEnumerable<Renderer> Renderers(GameObject device)
            => device == null
                ? new Renderer[0]
                : (IEnumerable<Renderer>)device.GetComponentsInChildren<Renderer>(includeInactive: true);
    }
}
