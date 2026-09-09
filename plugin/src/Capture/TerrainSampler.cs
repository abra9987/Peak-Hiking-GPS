using System;
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

        public static Heightfield Sample(CaptureFrame frame, int resolution, int layerMask)
        {
            var field = new Heightfield
            {
                Width = resolution,
                Depth = resolution,
                Heights = new float[resolution * resolution],
                Hit = new bool[resolution * resolution]
            };

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
                        // Collider reference just to test for a miss.
                        if (hit.colliderInstanceID == 0) continue;

                        int index = (rowStart * resolution) + i;
                        float y = hit.point.y;

                        field.Heights[index] = y;
                        field.Hit[index] = true;
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
