using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>Result of exporting one segment's geometry.</summary>
    internal sealed class MeshExportResult
    {
        public int VertexCount;
        public int TriangleCount;
        public int MeshCount;
        public int SkippedCount;
        public bool HasColors;
    }

    /// <summary>
    /// Writes a segment's real geometry to disk: the actual game meshes, in
    /// world space, with their vertex colours.
    ///
    /// This is the answer to what a heightfield cannot do. One altitude per XZ
    /// has no way to express a cave, an overhang or a tunnel; the meshes have
    /// them because they are the same triangles the game draws.
    ///
    /// Colour comes from the vertices rather than a texture because PEAK's
    /// terrain has no texture — the ground is shaded by triplanar shaders
    /// (W/Peak_Rock, W/Peak_Standard) driven by per-vertex data. That data is
    /// on the mesh, so exporting it carries the game's own colouring across
    /// without reimplementing a single shader.
    ///
    /// File layout, little-endian:
    ///   "PKMI"      4 bytes magic
    ///   version     uint32 = 1
    ///   vertices    uint32
    ///   indices     uint32
    ///   flags       uint32   bit 0: colours present
    ///   positions   float32 x3 per vertex   (Unity world space)
    ///   colours     uint8   x4 per vertex   (only when flagged)
    ///   indices     uint32  per index
    ///
    /// Normals are not stored. They are a third of the payload and the client
    /// derives them from the triangles, which for terrain is indistinguishable.
    /// </summary>
    internal static class MeshExporter
    {
        /// <summary>Shaders whose geometry costs far more than it shows on a map.</summary>
        private static readonly string[] SkippedShaders = { "FoliageGD", "W/Vine", "W/Character", "PlayerGhost" };

        public static MeshExportResult Export(
            GameObject segmentRoot,
            string path,
            float minSize,
            int triangleBudget,
            int lodLevel,
            bool includeFoliage)
        {
            var result = new MeshExportResult();
            if (segmentRoot == null) return result;

            List<Renderer> renderers = SelectRenderers(segmentRoot, minSize, lodLevel, includeFoliage, result);

            var positions = new List<Vector3>();
            var colors = new List<Color32>();
            var indices = new List<int>();

            foreach (Renderer renderer in renderers)
            {
                if (indices.Count / 3 >= triangleBudget) break;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                MeshData data = MeshReader.Read(mesh);
                if (data?.Positions == null || data.Indices == null || data.Indices.Length == 0)
                {
                    result.SkippedCount++;
                    continue;
                }

                int baseIndex = positions.Count;
                Transform transform = renderer.transform;
                MaterialPalette palette = MaterialPalette.Read(renderer.sharedMaterial);

                for (int v = 0; v < data.Positions.Length; v++)
                {
                    positions.Add(transform.TransformPoint(data.Positions[v]));

                    Vector3 normal = data.Normals != null && v < data.Normals.Length
                        ? transform.TransformDirection(data.Normals[v]).normalized
                        : Vector3.up;

                    Color32 weights = data.Colors != null && v < data.Colors.Length
                        ? data.Colors[v]
                        : new Color32(0, 0, 0, 255);

                    colors.Add(Evaluate(palette, weights, normal));
                }

                result.HasColors = true;

                for (int i = 0; i < data.Indices.Length; i++)
                {
                    int index = data.Indices[i] + baseIndex;
                    // A corrupt or misdecoded buffer would otherwise write
                    // indices that crash the viewer rather than fail here.
                    if (index < 0 || index >= positions.Count) { index = baseIndex; }
                    indices.Add(index);
                }

                result.MeshCount++;
            }

            Write(path, positions, colors, indices, result.HasColors);

            result.VertexCount = positions.Count;
            result.TriangleCount = indices.Count / 3;
            return result;
        }

        /// <summary>
        /// The blend parameters of one PEAK terrain material.
        ///
        /// W/Peak_Rock layers three textured colours over a base and blends
        /// them by the mesh's vertex colours, then lays a separate "top"
        /// colour over upward-facing surfaces — which is how snow sits on
        /// ledges while the cliff below stays rock. Reading those parameters
        /// and doing the same arithmetic reproduces the game's colouring
        /// per vertex, with no projection and therefore no smearing of the
        /// ground's colour down a vertical face.
        /// </summary>
        private struct MaterialPalette
        {
            public Color Base, Layer1, Layer2, Layer3, Top, Tint;
            public float VertexAmount;
            public bool Valid;

            public static MaterialPalette Read(Material material)
            {
                var palette = new MaterialPalette
                {
                    Base = Get(material, "_BaseColor", Color.grey),
                    Layer1 = Get(material, "_Color1", Color.grey),
                    // Not a typo in this project: the shader calls the second
                    // layer _Color21.
                    Layer2 = Get(material, "_Color21", Color.grey),
                    Layer3 = Get(material, "_Color3", Color.grey),
                    Top = Get(material, "_TopColor", Color.white),
                    Tint = Get(material, "_Tint", Color.white),
                    VertexAmount = material.HasProperty("_VertexColorAmount")
                        ? material.GetFloat("_VertexColorAmount")
                        : 1f,
                    Valid = material.HasProperty("_BaseColor") || material.HasProperty("_Color1")
                };

                return palette;
            }

            private static Color Get(Material material, string name, Color fallback)
                => material.HasProperty(name) ? material.GetColor(name) : fallback;
        }

        /// <summary>
        /// Evaluates a vertex's colour the way the terrain shader would.
        ///
        /// Vertex colours are weights, not colour: using them directly painted
        /// the entire mountain lilac. Sampling a top-down photograph instead
        /// would colour a cliff face with whatever sits on the clifftop, which
        /// on a game about climbing is exactly the surface that matters.
        /// </summary>
        private static Color32 Evaluate(MaterialPalette palette, Color32 weights, Vector3 normal)
        {
            if (!palette.Valid)
                return new Color32(weights.r, weights.g, weights.b, 255);

            float amount = Mathf.Clamp01(palette.VertexAmount);
            Color colour = palette.Base;

            colour = Color.Lerp(colour, palette.Layer1, (weights.r / 255f) * amount);
            colour = Color.Lerp(colour, palette.Layer2, (weights.g / 255f) * amount);
            colour = Color.Lerp(colour, palette.Layer3, (weights.b / 255f) * amount);

            // Upward faces take the top layer, the way snow and moss settle.
            float up = Mathf.Clamp01(normal.y);
            colour = Color.Lerp(colour, palette.Top, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.9f, up)));

            colour *= palette.Tint;

            return new Color32(
                (byte)(Mathf.Clamp01(colour.r) * 255f),
                (byte)(Mathf.Clamp01(colour.g) * 255f),
                (byte)(Mathf.Clamp01(colour.b) * 255f),
                255);
        }

        /// <summary>
        /// Chooses what is worth sending to a browser.
        ///
        /// LOD groups contribute the requested level. Exporting the coarsest
        /// is what made an earlier build look like cheap low-poly: that level
        /// exists to be drawn at a distance, and a map that lets you zoom in
        /// shows it for what it is. Loose meshes are kept only when large
        /// enough to be a landmark, which drops thousands of pebbles.
        /// </summary>
        private static List<Renderer> SelectRenderers(GameObject root, float minSize, int lodLevel, bool includeFoliage, MeshExportResult result)
        {
            var chosen = new List<Renderer>();
            var claimed = new HashSet<Renderer>();

            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(includeInactive: false))
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0) continue;

                foreach (LOD lod in lods)
                    foreach (Renderer r in lod.renderers)
                        if (r != null) claimed.Add(r);

                int level = Mathf.Clamp(lodLevel, 0, lods.Length - 1);
                foreach (Renderer r in lods[level].renderers)
                {
                    if (r == null || !Keep(r, includeFoliage)) continue;
                    chosen.Add(r);
                }
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: false))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || claimed.Contains(renderer) || !Keep(renderer, includeFoliage)) continue;

                Vector3 size = renderer.bounds.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < minSize)
                {
                    result.SkippedCount++;
                    continue;
                }

                chosen.Add(renderer);
            }

            // Biggest first, so a triangle budget spends itself on landmarks
            // rather than on whatever happened to be enumerated first.
            chosen.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
            return chosen;
        }

        private static bool Keep(Renderer renderer, bool includeFoliage)
        {
            if (!renderer.enabled) return false;
            if (includeFoliage) return true;

            Material material = renderer.sharedMaterial;
            if (material?.shader == null) return true;

            string shader = material.shader.name;
            for (int i = 0; i < SkippedShaders.Length; i++)
                if (shader.Contains(SkippedShaders[i])) return false;

            return true;
        }

        private static void Write(
            string path,
            List<Vector3> positions,
            List<Color32> colors,
            List<int> indices,
            bool hasColors)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'P', (byte)'K', (byte)'M', (byte)'I' });
                writer.Write(1u);
                writer.Write((uint)positions.Count);
                writer.Write((uint)indices.Count);
                writer.Write(hasColors ? 1u : 0u);

                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 p = positions[i];
                    writer.Write(p.x); writer.Write(p.y); writer.Write(p.z);
                }

                if (hasColors)
                {
                    for (int i = 0; i < colors.Count; i++)
                    {
                        Color32 c = colors[i];
                        writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a);
                    }
                }

                for (int i = 0; i < indices.Count; i++)
                    writer.Write((uint)indices[i]);
            }
        }
    }
}
