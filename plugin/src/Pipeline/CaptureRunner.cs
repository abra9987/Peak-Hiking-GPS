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
            int layerMask = BuildRaycastMask();

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

            DescribeMap(snapshot.Map);
            OrthoCapture.LogLayers();
            OrthoCapture.LogLargeRenderers(800f);

            // Dismiss the loading screen before photographing anything.
            //
            // While it is up the world is simply not drawn, which is what made
            // every capture route return black: render texture, render
            // request, back buffer, purpose-built camera and the game's own.
            // LoadingScreenHandler.loading going false is not the same thing —
            // the screen itself lingers, and a screen probe caught the plane
            // animation rather than a mountain.
            try
            {
                LoadingScreenHandler.KillCurrentLoadingScreen();
                Plugin.Logger.LogInfo("Dismissed the loading screen.");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"Could not dismiss the loading screen: {e.Message}");
            }

            yield return OrthoCapture.WaitForWorldToRender(15f);

            // What is actually on screen when a capture begins?
            if (Plugin.Settings.WriteDiagnostics.Value)
            {
                yield return new WaitForEndOfFrame();

                Texture2D probe = ScreenCapture.CaptureScreenshotAsTexture();
                if (probe != null)
                {
                    File.WriteAllBytes(Path.Combine(snapshotDir, "screen-probe.jpg"), probe.EncodeToJPG(85));
                    Plugin.Logger.LogInfo($"Screen probe written: {probe.width}x{probe.height}.");
                    UnityEngine.Object.Destroy(probe);
                }
                else
                {
                    Plugin.Logger.LogWarning("Screen probe returned nothing.");
                }
            }

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

                // One segment's worth of shader detail is enough to work from,
                // and six would bury the log.
                if (index == 0 && Plugin.Settings.WriteDiagnostics.Value)
                    MeshSurvey.Survey(preparer.Root, index, dto.Biome);

                // --- Heightfield ---------------------------------------------
                Heightfield field = TerrainSampler.Sample(frame, heightRes, layerMask);
                string heightFile = $"segment_{index}.height.bin";
                SnapshotWriter.WriteHeightfield(
                    Path.Combine(snapshotDir, heightFile),
                    field.Heights, field.Hit, field.Min, field.Max);

                string groundFile = $"segment_{index}.ground.bin";
                SnapshotWriter.WriteGroundColors(
                    Path.Combine(snapshotDir, groundFile), field.Colors, field.Hit);

                dto.Terrain = new TerrainDto
                {
                    File = heightFile,
                    ColorFile = groundFile,
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
                bool albedoWritten = false;

                // The photograph is kept rather than written and dropped: the
                // mesh export samples it for vertex colour, which is how the
                // model ends up looking like the game rather than like a
                // guess.
                Texture2D albedoTexture = null;
                yield return OrthoCapture.Capture(frame, albedoRes, ~0, t => albedoTexture = t);

                if (albedoTexture != null)
                {
                    SnapshotWriter.WriteAlbedo(Path.Combine(snapshotDir, albedoFile), albedoTexture);
                    albedoWritten = true;
                }

                // The orthophoto is optional. Terrain shape, altitudes and
                // markers are the substance; colour is decoration the client
                // can derive from the heightfield when it is missing.
                dto.Albedo = albedoWritten
                    ? new AlbedoDto
                    {
                        File = albedoFile,
                        Resolution = albedoRes,
                        Origin = SnapshotWriter.Vec2(frame.OriginX, frame.OriginZ),
                        Size = SnapshotWriter.Vec2(frame.SizeX, frame.SizeZ)
                    }
                    : null;

                // --- Real geometry -------------------------------------------
                int onlySegment = Plugin.Settings.MeshOnlySegment.Value;
                if (Plugin.Settings.ExportMeshes.Value && (onlySegment < 0 || onlySegment == index))
                {
                    string meshFile = $"segment_{index}.mesh.bin";
                    MeshExportResult exported = MeshExporter.Export(
                        preparer.Root,
                        Path.Combine(snapshotDir, meshFile),
                        Plugin.Settings.MeshMinSize.Value,
                        Plugin.Settings.MeshTriangleBudget.Value,
                        Plugin.Settings.MeshLodLevel.Value,
                        Plugin.Settings.MeshIncludeFoliage.Value);

                    if (exported.TriangleCount > 0)
                    {
                        dto.Mesh = new MeshDto
                        {
                            File = meshFile,
                            VertexCount = exported.VertexCount,
                            TriangleCount = exported.TriangleCount,
                            MeshCount = exported.MeshCount,
                            HasColors = exported.HasColors
                        };

                        Plugin.Logger.LogInfo(
                            $"  mesh: {exported.TriangleCount / 1000}k tris from {exported.MeshCount} objects, " +
                            $"{exported.VertexCount / 1000}k verts, colours={exported.HasColors}, " +
                            $"{exported.SkippedCount} filtered out, " +
                            $"LOST {exported.LostTriangles / 1000}k tris in {exported.LostMeshes} unreadable meshes");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning("  mesh export produced nothing.");
                    }
                }

                if (albedoTexture != null) UnityEngine.Object.Destroy(albedoTexture);

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

        /// <summary>
        /// The layers a surface ray is allowed to hit.
        ///
        /// Water is excluded, and it matters more than it sounds: PEAK puts a
        /// 5000 x 1000 x 5000 box collider named Misc/Water/Collision at
        /// y = -1 across the whole world. Every ray that misses the mountain
        /// hits it instead, which reported a flawless "100% coverage" while
        /// 78% of the segment was actually a flat sheet of sea floor with a
        /// mountain poking through.
        /// </summary>
        private static int BuildRaycastMask()
        {
            int mask = Physics.DefaultRaycastLayers;

            // Water: a world-spanning box at y = -1 that every stray ray hits.
            // InvisWall: the invisible barriers around a segment, which the
            // viewer would otherwise render as broad flat sheets of "ground".
            foreach (string layerName in new[] { "Water", "InvisWall" })
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer < 0) continue;

                mask &= ~(1 << layer);
                Plugin.Logger.LogInfo($"Excluding layer '{layerName}' ({layer}) from surface sampling.");
            }

            return mask;
        }

        /// <summary>
        /// Records which of PEAK's daily maps was captured, and where the
        /// index came from. See <see cref="MapDto"/> for why that matters.
        /// </summary>
        private static void DescribeMap(MapDto map)
        {
            map.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            try
            {
                NextLevelService service = GameHandler.GetService<NextLevelService>();
                if (service == null)
                {
                    map.IndexSource = "unavailable";
                    return;
                }

                map.LevelIndex = service.NextLevelIndexOrFallback;
                map.OfflineLevelIndex = service.OfflineLevelIndex;
                map.IndexSource = service.HasReceivedLevelIndex ? "server" : "offline";

                if (map.LevelIndex != map.OfflineLevelIndex)
                {
                    Plugin.Logger.LogWarning(
                        $"Level index {map.LevelIndex} ({map.IndexSource}) disagrees with the " +
                        $"locally computed {map.OfflineLevelIndex}; captured map may not be the one others see.");
                }
            }
            catch (Exception e)
            {
                map.IndexSource = "unavailable";
                Plugin.Logger.LogWarning($"Could not read the level index: {e.Message}");
            }

            try
            {
                string[] scenes = SingletonAsset<MapBaker>.Instance?.ScenePaths;
                if (scenes != null && scenes.Length > 0)
                {
                    map.ScenePoolSize = scenes.Length;
                    map.PoolIndex = ((map.LevelIndex % scenes.Length) + scenes.Length) % scenes.Length;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"Could not read the scene pool: {e.Message}");
            }

            Plugin.Logger.LogInfo(
                $"Map: scene '{map.SceneName}', levelIndex {map.LevelIndex} (source: {map.IndexSource}), " +
                $"pool {map.PoolIndex}/{map.ScenePoolSize}.");
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
