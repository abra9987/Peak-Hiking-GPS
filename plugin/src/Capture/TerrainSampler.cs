using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>Result of sampling one segment's surface.</summary>
    internal sealed class Heightfield
    {
        public int Width;
        public int Depth;
        public float[] Heights;   // world Y, meaningless where Hit is false
        public bool[] Hit;
        public float Min = float.PositiveInfinity;
        public float Max = float.NegativeInfinity;
        public float Coverage;    // fraction of samples that hit geometry

        /// <summary>
        /// Ground colour per sample, taken from the material the ray landed on.
        ///
        /// In PEAK the walkable surface is the "top" layer of the terrain
        /// shader — sand on the shore, snow higher up — laid over whatever rock
        /// is underneath. A downward ray lands on exactly that surface, so
        /// reading the material's top colour where it lands reproduces the
        /// ground the player actually walks on.
        /// </summary>
        public Color32[] Colors;
    }

    /// <summary>
    /// Samples the surface of a segment by firing a grid of downward rays.
    ///
    /// Raycasting rather than reading Unity Terrain heightmaps is deliberate:
    /// PEAK's mountains mix terrain, meshes and props, and a ray hits whatever
    /// the player would actually stand on. It also means the exporter keeps
    /// working if the game switches its terrain representation.
    ///
    /// Rays are issued through <see cref="RaycastCommand"/>, which runs the
    /// batch across worker threads. A 1024x1024 grid is over a million queries;
    /// issuing them one by one on the main thread takes tens of seconds, the
    /// batched form takes well under one.
    /// </summary>
    internal static class TerrainSampler
    {
        /// <summary>Rows sampled per batch, to cap peak native memory.</summary>
        private const int RowsPerBatch = 64;

        /// <summary>
        /// Colliders that span the whole frame and are essentially flat: the
        /// ocean, kill planes, void floors.
        ///
        /// These matter enormously. PEAK has a plane at y = -1 covering the
        /// entire world, so without this filter every ray that misses the
        /// mountain still reports a hit — the first capture run measured
        /// "100% coverage" with 77-85% of every segment sitting at exactly
        /// -1.0, which is a flat sheet with a mountain poking through it, not
        /// a mountain.
        ///
        /// Identified by geometry rather than by name, so it keeps working
        /// when the game renames things, with names only as a second signal.
        /// </summary>
        private static HashSet<int> FindSpanningPlanes(CaptureFrame frame)
        {
            var excluded = new HashSet<int>();
            var colliders = UnityEngine.Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            float flatEnough = Mathf.Max(5f, frame.Height * 0.02f);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;

                Bounds b = collider.bounds;
                bool spansFrame = b.size.x >= frame.SizeX * 0.9f && b.size.z >= frame.SizeZ * 0.9f;
                if (!spansFrame) continue;

                string name = collider.gameObject.name.ToLowerInvariant();
                bool looksLikeWater =
                    name.Contains("water") || name.Contains("ocean") || name.Contains("sea") ||
                    name.Contains("void") || name.Contains("kill") || name.Contains("death");

                if (b.size.y > flatEnough && !looksLikeWater) continue;

                excluded.Add(collider.GetInstanceID());
                Plugin.Logger.LogInfo(
                    $"  ignoring spanning collider '{collider.gameObject.name}' " +
                    $"({b.size.x:F0} x {b.size.y:F0} x {b.size.z:F0} at y={b.center.y:F1})");
            }

            return excluded;
        }

        /// <summary>
        /// Reports what is being hit when a large share of a segment measures
        /// at the same height.
        ///
        /// The first capture runs put 77-85% of every segment at exactly -1.0,
        /// and the geometric filter above did not recognise whatever produces
        /// it. Rather than guess again, this names the collider — object,
        /// layer, hierarchy path and bounds — so the filter can be aimed at
        /// the real thing.
        /// </summary>
        private static void DiagnoseFloor(Heightfield field, CaptureFrame frame, int resolution, int layerMask)
        {
            int atFloor = 0;
            var samples = new List<int>();

            for (int i = 0; i < field.Heights.Length; i++)
            {
                if (!field.Hit[i]) continue;
                if (field.Heights[i] - field.Min > 1f) continue;

                atFloor++;
                if (samples.Count < 4 && (atFloor % 997) == 1) samples.Add(i);
            }

            float share = field.Heights.Length > 0 ? (float)atFloor / field.Heights.Length : 0f;
            if (share < 0.25f) return;

            Plugin.Logger.LogWarning(
                $"  {share:P1} of samples sit within 1m of y={field.Min:F1} - probing what they hit:");

            float stepX = frame.SizeX / resolution;
            float stepZ = frame.SizeZ / resolution;

            foreach (int index in samples)
            {
                int x = index % resolution;
                int z = index / resolution;
                var origin = new Vector3(
                    frame.OriginX + (x + 0.5f) * stepX,
                    frame.MaxY + 50f,
                    frame.OriginZ + (z + 0.5f) * stepZ);

                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                        frame.Height + 100f, layerMask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Collider c = hit.collider;
                if (c == null) continue;

                Plugin.Logger.LogWarning(
                    $"    y={hit.point.y:F2} '{Path(c.transform)}' " +
                    $"layer={LayerMask.LayerToName(c.gameObject.layer)}({c.gameObject.layer}) " +
                    $"type={c.GetType().Name} bounds={c.bounds.size.x:F0}x{c.bounds.size.y:F0}x{c.bounds.size.z:F0}");
            }
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform current = t; current != null && parts.Count < 6; current = current.parent)
                parts.Add(current.name);

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Maps every collider to the ground colour of its material.
        ///
        /// Built once per segment: a batched raycast reports only a collider
        /// id, and looking the managed object up a million times would cost far
        /// more than the whole sampling pass.
        /// </summary>
        private static Dictionary<int, Color32> MapColliderColors()
        {
            var map = new Dictionary<int, Color32>();

            foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (collider == null) continue;

                // The renderer is not reliably on the collider's own object:
                // terrain chunks often hang the collider off a child or share
                // one with a parent. Looking only at the same GameObject left
                // most of the ground grey.
                Renderer renderer = collider.GetComponent<Renderer>()
                                    ?? collider.GetComponentInParent<Renderer>()
                                    ?? collider.GetComponentInChildren<Renderer>();

                Material material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null) continue;

                Color colour;
                if (material.HasProperty("_TopColor")) colour = material.GetColor("_TopColor");
                else if (material.HasProperty("_BaseColor")) colour = material.GetColor("_BaseColor");
                else continue;

                if (material.HasProperty("_Tint")) colour *= material.GetColor("_Tint");

                map[collider.GetInstanceID()] = new Color32(
                    (byte)(Mathf.Clamp01(colour.r) * 255f),
                    (byte)(Mathf.Clamp01(colour.g) * 255f),
                    (byte)(Mathf.Clamp01(colour.b) * 255f),
                    255);
            }

            return map;
        }

        public static Heightfield Sample(CaptureFrame frame, int resolution, int layerMask)
        {
            var field = new Heightfield
            {
                Width = resolution,
                Depth = resolution,
                Heights = new float[resolution * resolution],
                Hit = new bool[resolution * resolution],
                Colors = new Color32[resolution * resolution]
            };

            HashSet<int> ignored = FindSpanningPlanes(frame);
            Dictionary<int, Color32> groundColors = MapColliderColors();

            // Sample at cell centres so the field is symmetric about the frame.
            float stepX = frame.SizeX / resolution;
            float stepZ = frame.SizeZ / resolution;
            float castTop = frame.MaxY + 50f;
            float castDistance = frame.Height + 100f;

            var queryParams = new QueryParameters(
                layerMask: layerMask,
                hitMultipleFaces: false,
                hitTriggers: QueryTriggerInteraction.Ignore,
                hitBackfaces: false);

            int hits = 0;

            for (int rowStart = 0; rowStart < resolution; rowStart += RowsPerBatch)
            {
                int rows = Math.Min(RowsPerBatch, resolution - rowStart);
                int count = rows * resolution;

                var commands = new NativeArray<RaycastCommand>(count, Allocator.TempJob);
                var results = new NativeArray<RaycastHit>(count, Allocator.TempJob);

                try
                {
                    for (int r = 0; r < rows; r++)
                    {
                        int z = rowStart + r;
                        float worldZ = frame.OriginZ + (z + 0.5f) * stepZ;
                        int rowOffset = r * resolution;

                        for (int x = 0; x < resolution; x++)
                        {
                            float worldX = frame.OriginX + (x + 0.5f) * stepX;
                            commands[rowOffset + x] = new RaycastCommand(
                                new Vector3(worldX, castTop, worldZ),
                                Vector3.down,
                                queryParams,
                                castDistance);
                        }
                    }

                    JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 128, 1);
                    handle.Complete();

                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = results[i];
                        // colliderInstanceID avoids resolving the managed
                        // Collider reference just to test for a miss, and lets
                        // the spanning-plane filter work without touching the
                        // managed object at all.
                        if (hit.colliderInstanceID == 0) continue;
                        if (ignored.Contains(hit.colliderInstanceID)) continue;

                        int index = (rowStart * resolution) + i;
                        float y = hit.point.y;

                        field.Heights[index] = y;
                        field.Hit[index] = true;
                        field.Colors[index] = groundColors.TryGetValue(hit.colliderInstanceID, out Color32 ground)
                            ? ground
                            : new Color32(150, 150, 150, 255);
                        if (y < field.Min) field.Min = y;
                        if (y > field.Max) field.Max = y;
                        hits++;
                    }
                }
                finally
                {
                    commands.Dispose();
                    results.Dispose();
                }
            }

            int total = resolution * resolution;
            field.Coverage = total > 0 ? (float)hits / total : 0f;

            DiagnoseFloor(field, frame, resolution, layerMask);

            if (hits == 0)
            {
                // Keep the manifest well-formed rather than emitting infinities.
                field.Min = frame.MinY;
                field.Max = frame.MaxY;
            }

            return field;
        }
    }
}
