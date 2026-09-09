using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PeakMapInteractive.Capture;
using PeakMapInteractive.Collect;
using PeakMapInteractive.Export;
using UnityEngine;
using Zorro.Core;

namespace PeakMapInteractive.Pipeline
{
    /// <summary>
    /// Drives a full capture: every segment measured, photographed and
    /// catalogued into one snapshot directory.
    /// </summary>
    internal static class CaptureRunner
    {
        public static bool IsRunning { get; private set; }
        public static bool HasCompleted { get; private set; }

        public static IEnumerator Run()
        {
            if (IsRunning)
            {
                Plugin.Logger.LogWarning("Capture already running; ignoring request.");
                yield break;
            }

            IsRunning = true;
            float startedAt = Time.realtimeSinceStartup;

            MapHandler mapHandler = Singleton<MapHandler>.Instance;
            if (mapHandler?.segments == null)
            {
                Plugin.Logger.LogError("MapHandler has no segments; nothing to capture.");
                IsRunning = false;
                yield break;
            }

            int segmentCount = mapHandler.segments.Length;
            Plugin.Logger.LogInfo($"Capturing {segmentCount} segments.");

            string snapshotDir = Path.Combine(Plugin.OutputDir, "snapshot");
            Directory.CreateDirectory(snapshotDir);

            int heightRes = Plugin.Settings.HeightResolution.Value;
            int albedoRes = Plugin.Settings.AlbedoResolution.Value;
            float padding = Plugin.Settings.BoundsPadding.Value;
            int layerMask = Physics.DefaultRaycastLayers;

            var snapshot = new Snapshot
            {
                GeneratedAt = SnapshotWriter.Timestamp(),
                GameVersion = Application.version,
                Capture =
                {
                    HeightResolution = heightRes,
                    AlbedoResolution = albedoRes,
                    RaycastLayerMask = layerMask
                }
            };

            var collector = new MarkerCollector();

            for (int index = 0; index < segmentCount; index++)
            {
                var preparer = new SegmentPreparer();
                SegmentDto dto = null;

                try
                {
                    MapHandler.MapSegment segment = mapHandler.segments[index];
                    if (segment?.segmentParent == null)
                    {
                        Plugin.Logger.LogWarning($"Segment {index} has no parent object; skipped.");
                        continue;
                    }

                    preparer.Prepare(
                        segment.segmentParent,
                        segment.segmentCampfire,
                        segment.wallNext,
                        segment.wallPrevious);

                    dto = new SegmentDto
                    {
                        Index = index,
                        Biome = segment.biome.ToString(),
                        DisplayName = Prettify(segment.biome.ToString())
                    };
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError($"Segment {index} preparation failed: {e}");
                    preparer.Restore();
                    continue;
                }

                // Let streaming and spawners settle before measuring anything.
                preparer.SpawnItems();
                yield return null;
                yield return new WaitForEndOfFrame();

                if (!SegmentBounds.TryCompute(preparer.Root, out Bounds bounds))
                {
                    Plugin.Logger.LogWarning($"Segment {index} has no measurable geometry; skipped.");
                    preparer.Restore();
                    continue;
                }

                CaptureFrame frame = CaptureFrame.FromBounds(bounds, padding);
                Plugin.Logger.LogInfo($"Segment {index} ({dto.Biome}): {frame}");

                dto.Bounds = new BoundsDto
                {
                    Min = SnapshotWriter.Vec3(frame.Min),
                    Max = SnapshotWriter.Vec3(frame.Max)
                };

                // --- Heightfield ---------------------------------------------
                Heightfield field = TerrainSampler.Sample(frame, heightRes, layerMask);
                string heightFile = $"segment_{index}.height.bin";
                SnapshotWriter.WriteHeightfield(
                    Path.Combine(snapshotDir, heightFile),
                    field.Heights, field.Hit, field.Min, field.Max);

                dto.Terrain = new TerrainDto
                {
                    File = heightFile,
                    Width = field.Width,
                    Depth = field.Depth,
                    Origin = SnapshotWriter.Vec2(frame.OriginX, frame.OriginZ),
                    Size = SnapshotWriter.Vec2(frame.SizeX, frame.SizeZ),
                    HeightMin = field.Min,
                    HeightMax = field.Max,
                    Coverage = field.Coverage
                };

                Plugin.Logger.LogInfo(
                    $"  heightfield {field.Width}x{field.Depth}, " +
                    $"coverage {field.Coverage:P1}, y=[{field.Min:F1} .. {field.Max:F1}]");

                // --- Orthophoto ----------------------------------------------
                string albedoFile = $"segment_{index}.albedo.jpg";
                yield return OrthoCapture.Capture(frame, albedoRes, ~0, texture =>
                {
                    try
                    {
                        SnapshotWriter.WriteAlbedo(Path.Combine(snapshotDir, albedoFile), texture);
                    }
                    finally
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                });

                dto.Albedo = new AlbedoDto
                {
                    File = albedoFile,
                    Resolution = albedoRes,
                    Origin = SnapshotWriter.Vec2(frame.OriginX, frame.OriginZ),
                    Size = SnapshotWriter.Vec2(frame.SizeX, frame.SizeZ)
                };

                // --- Markers -------------------------------------------------
                dto.Markers = collector.Collect(preparer.Root, index);
                Plugin.Logger.LogInfo($"  {dto.Markers.Count} markers");

                snapshot.Segments.Add(dto);
                preparer.Restore();
                yield return null;
            }

            SnapshotWriter.WriteManifest(Path.Combine(snapshotDir, "snapshot.json"), snapshot);

            if (Plugin.Settings.WriteDiagnostics.Value)
                WriteDiagnostics(snapshotDir, collector);

            float elapsed = Time.realtimeSinceStartup - startedAt;
            Plugin.Logger.LogInfo(
                $"Snapshot written to {snapshotDir} " +
                $"({snapshot.Segments.Count} segments, {elapsed:F1}s).");

            IsRunning = false;
            HasCompleted = true;

            if (Plugin.Settings.QuitWhenDone.Value)
            {
                yield return null;
                Plugin.Logger.LogInfo("QuitWhenDone is set; exiting.");
                Application.Quit();
            }
        }

        private static void WriteDiagnostics(string dir, MarkerCollector collector)
        {
            var payload = new Dictionary<string, object>
            {
                ["unmatchedComponentTypes"] = collector.UnknownTypes
            };

            File.WriteAllText(
                Path.Combine(dir, "diagnostics.json"),
                JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        /// <summary>Turns an enum name into something a person would read.</summary>
        private static string Prettify(string enumName)
        {
            if (string.IsNullOrEmpty(enumName)) return enumName;

            var sb = new System.Text.StringBuilder(enumName.Length + 4);
            for (int i = 0; i < enumName.Length; i++)
            {
                char c = enumName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(enumName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
