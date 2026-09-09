using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Establishes whether the segment's real geometry can be exported, and at
    /// what size.
    ///
    /// A heightfield stores one altitude per XZ, so it cannot represent a cave,
    /// an overhang or a tunnel — exactly the features that make PEAK worth
    /// mapping in three dimensions. Exporting meshes keeps them, and this
    /// measures the three things that decide whether that is practical:
    ///
    ///  - size, counted at the *lowest* LOD rather than the one the player
    ///    sees, because 84M triangles at LOD0 says nothing about what a browser
    ///    would actually be sent;
    ///  - readability, including whether the vertex buffer can be reached for
    ///    meshes marked Read/Write Disabled, which the mesh API refuses;
    ///  - colour, since the terrain has no base texture and is shaded from
    ///    vertex data by custom triplanar shaders. If that data is on the mesh
    ///    it can be exported; if it is only in shader properties, those can.
    /// </summary>
    internal static class MeshSurvey
    {
        public static void Survey(GameObject segmentRoot, int index, string biome)
        {
            if (segmentRoot == null) return;

            SurveyLods(segmentRoot, index, biome);
            SurveyVertexData(segmentRoot);
            SurveyTerrainMaterials(segmentRoot);
        }

        /// <summary>Triangle budget at the finest and coarsest LOD.</summary>
        private static void SurveyLods(GameObject root, int index, string biome)
        {
            var groups = root.GetComponentsInChildren<LODGroup>(includeInactive: false);

            long finest = 0;
            long coarsest = 0;
            int meshesInGroups = 0;

            foreach (LODGroup group in groups)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0) continue;

                finest += TriangleCount(lods[0].renderers, ref meshesInGroups);
                int dummy = 0;
                coarsest += TriangleCount(lods[lods.Length - 1].renderers, ref dummy);
            }

            // Anything not under a LOD group is shipped as-is at every quality.
            var inGroups = new HashSet<Renderer>();
            foreach (LODGroup group in groups)
                foreach (LOD lod in group.GetLODs())
                    foreach (Renderer r in lod.renderers)
                        if (r != null) inGroups.Add(r);

            long loose = 0;
            int looseCount = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || inGroups.Contains(renderer)) continue;
                if (filter.sharedMesh == null) continue;

                loose += EstimateTriangles(filter.sharedMesh);
                looseCount++;
            }

            Plugin.Logger.LogInfo(
                $"  LOD survey [{index} {biome}]: {groups.Length} LOD groups " +
                $"(finest {finest / 1000}k tris, coarsest {coarsest / 1000}k tris), " +
                $"{looseCount} un-grouped meshes at {loose / 1000}k tris. " +
                $"Coarsest total: {(coarsest + loose) / 1000}k tris.");
        }

        private static long TriangleCount(Renderer[] renderers, ref int meshes)
        {
            long total = 0;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                total += EstimateTriangles(filter.sharedMesh);
                meshes++;
            }

            return total;
        }

        /// <summary>
        /// Index count works without the mesh being readable, so this counts
        /// unreadable terrain too.
        /// </summary>
        private static long EstimateTriangles(Mesh mesh)
        {
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                indices += (long)mesh.GetIndexCount(i);

            return indices / 3;
        }

        /// <summary>
        /// What the mesh actually carries, and whether an unreadable one can
        /// still be reached through its GPU buffers.
        /// </summary>
        private static void SurveyVertexData(GameObject root)
        {
            Mesh sample = null;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.isReadable) continue;
                if (mesh.vertexCount < 5000) continue; // want a terrain shell, not a pebble

                sample = mesh;
                break;
            }

            if (sample == null)
            {
                Plugin.Logger.LogInfo("  no large unreadable mesh found to probe.");
                return;
            }

            var attributes = sample.GetVertexAttributes()
                .Select(a => $"{a.attribute}:{a.format}x{a.dimension}")
                .ToArray();

            Plugin.Logger.LogInfo(
                $"  unreadable sample '{sample.name}': {sample.vertexCount} verts, " +
                $"attributes = {string.Join(", ", attributes)}");

            // The decisive question: can the vertices be read anyway?
            try
            {
                sample.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                using (GraphicsBuffer buffer = sample.GetVertexBuffer(0))
                {
                    Plugin.Logger.LogInfo(buffer == null
                        ? "  GPU vertex buffer: NOT AVAILABLE"
                        : $"  GPU vertex buffer: available, {buffer.count} elements x {buffer.stride} bytes");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"  GPU vertex buffer unavailable: {e.Message}");
            }
        }

        /// <summary>
        /// Colour lives in shader properties when it is not in a texture.
        /// Dumps what the terrain materials actually hold.
        /// </summary>
        private static void SurveyTerrainMaterials(GameObject root)
        {
            var reported = new HashSet<string>();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null) continue;

                string shader = material.shader.name;
                if (shader != "W/Peak_Rock" && shader != "W/Peak_Standard") continue;
                if (!reported.Add(material.name)) continue;

                // Enumerate what the shader actually exposes rather than
                // guessing property names. Vertex colours turned out to be
                // splat weights, not colour, so the real colours have to come
                // from somewhere — and this says where.
                var parts = new List<string>();
                int count = material.shader.GetPropertyCount();

                for (int p = 0; p < count; p++)
                {
                    string name = material.shader.GetPropertyName(p);
                    UnityEngine.Rendering.ShaderPropertyType type = material.shader.GetPropertyType(p);

                    switch (type)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            parts.Add($"{name}={ColorUtility.ToHtmlStringRGB(material.GetColor(name))}");
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            Texture t = material.GetTexture(name);
                            if (t != null) parts.Add($"{name}=tex:{t.name}");
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            parts.Add($"{name}={material.GetFloat(name):F2}");
                            break;
                    }
                }

                Plugin.Logger.LogInfo($"  material '{material.name}' ({shader}):");
                foreach (string part in parts) Plugin.Logger.LogInfo($"      {part}");

                if (reported.Count >= 4) return;
            }
        }
    }
}
