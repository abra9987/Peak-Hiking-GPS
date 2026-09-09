using System.Collections.Generic;
using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Measures what it would take to export a segment's real geometry and
    /// materials, instead of a heightfield with a photo draped over it.
    ///
    /// A heightfield stores one altitude per XZ, so it cannot represent a cave,
    /// an overhang or a tunnel at all — the exact features that make PEAK worth
    /// mapping in three dimensions. Exporting meshes keeps them.
    ///
    /// Two things decide whether that is possible in a shipped build:
    /// meshes marked Read/Write Disabled cannot be read from script at all, and
    /// the total triangle count decides whether the result can be delivered to
    /// a browser. This reports both before any exporter is written.
    /// </summary>
    internal static class MeshSurvey
    {
        public static void Survey(GameObject segmentRoot, int index, string biome)
        {
            if (segmentRoot == null) return;

            var filters = segmentRoot.GetComponentsInChildren<MeshFilter>(includeInactive: false);

            int readable = 0;
            int unreadable = 0;
            long vertices = 0;
            long triangles = 0;

            var materials = new HashSet<string>();
            var textures = new HashSet<string>();
            var shaders = new HashSet<string>();
            var unreadableExamples = new List<string>();

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                if (mesh.isReadable)
                {
                    readable++;
                    vertices += mesh.vertexCount;
                    triangles += mesh.triangles.Length / 3;
                }
                else
                {
                    unreadable++;
                    vertices += mesh.vertexCount; // still reported by the mesh
                    if (unreadableExamples.Count < 5) unreadableExamples.Add(mesh.name);
                }

                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null) continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    materials.Add(material.name);
                    if (material.shader != null) shaders.Add(material.shader.name);

                    if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") is Texture baseMap)
                        textures.Add($"{baseMap.name}:{baseMap.width}x{baseMap.height}");
                    else if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") is Texture mainTex)
                        textures.Add($"{mainTex.name}:{mainTex.width}x{mainTex.height}");
                }
            }

            Plugin.Logger.LogInfo(
                $"  mesh survey [{index} {biome}]: {filters.Length} filters, " +
                $"{readable} readable / {unreadable} not, " +
                $"~{vertices / 1000}k verts, {triangles / 1000}k tris (readable only), " +
                $"{materials.Count} materials, {shaders.Count} shaders, {textures.Count} base textures");

            if (unreadableExamples.Count > 0)
                Plugin.Logger.LogInfo("    unreadable examples: " + string.Join(", ", unreadableExamples));

            int shown = 0;
            foreach (string shader in shaders)
            {
                Plugin.Logger.LogInfo("    shader: " + shader);
                if (++shown >= 6) break;
            }

            shown = 0;
            foreach (string texture in textures)
            {
                Plugin.Logger.LogInfo("    texture: " + texture);
                if (++shown >= 8) break;
            }
        }
    }
}
