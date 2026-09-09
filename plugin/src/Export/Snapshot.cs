using System.Collections.Generic;
using Newtonsoft.Json;

namespace PeakMapInteractive.Export
{
    // Plain DTOs mirroring docs/DATA-FORMAT.md. Field names are lowerCamelCase
    // to match the JSON contract consumed by the web client.

    public sealed class Snapshot
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion = 1;
        [JsonProperty("generatedAt")] public string GeneratedAt;
        [JsonProperty("gameVersion")] public string GameVersion;
        [JsonProperty("pluginVersion")] public string PluginVersion = Plugin.Version;
        [JsonProperty("map")] public MapDto Map = new MapDto();
        [JsonProperty("capture")] public CaptureSettings Capture = new CaptureSettings();
        [JsonProperty("segments")] public List<SegmentDto> Segments = new List<SegmentDto>();
    }

    /// <summary>
    /// Which daily map this is.
    ///
    /// PEAK picks the day's mountain by <c>levelIndex % scenePool</c>, where
    /// the index advances once every 24 hours from 2025-06-14 17:00 UTC. The
    /// index is normally handed out by the server and falls back to a locally
    /// computed one when offline — and the two can disagree, which is how two
    /// captures taken minutes apart ended up on different mountains.
    ///
    /// Recording it makes a snapshot verifiable: anyone can check which map
    /// was captured instead of trusting the timestamp.
    /// </summary>
    public sealed class MapDto
    {
        [JsonProperty("levelIndex")] public int LevelIndex;
        [JsonProperty("sceneName")] public string SceneName;
        /// <summary>"server" when the authoritative index was received, else "offline".</summary>
        [JsonProperty("indexSource")] public string IndexSource;
        [JsonProperty("offlineLevelIndex")] public int OfflineLevelIndex;

        /// <summary>
        /// How many maps exist in total.
        ///
        /// The day's mountain is <c>levelIndex % scenePoolSize</c> over scenes
        /// shipped inside the game, so the pool is finite and the whole
        /// rotation can be captured once instead of scraped daily.
        /// </summary>
        [JsonProperty("scenePoolSize")] public int ScenePoolSize;

        /// <summary>Index within the pool: what actually selects the scene.</summary>
        [JsonProperty("poolIndex")] public int PoolIndex;
    }

    public sealed class CaptureSettings
    {
        [JsonProperty("heightResolution")] public int HeightResolution;
        [JsonProperty("albedoResolution")] public int AlbedoResolution;
        [JsonProperty("raycastLayerMask")] public int RaycastLayerMask;
    }

    public sealed class SegmentDto
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("biome")] public string Biome;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("bounds")] public BoundsDto Bounds;
        [JsonProperty("terrain")] public TerrainDto Terrain;
        [JsonProperty("albedo")] public AlbedoDto Albedo;
        [JsonProperty("mesh")] public MeshDto Mesh;
        [JsonProperty("markers")] public List<MarkerDto> Markers = new List<MarkerDto>();
    }

    public sealed class BoundsDto
    {
        [JsonProperty("min")] public float[] Min;
        [JsonProperty("max")] public float[] Max;
    }

    public sealed class TerrainDto
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("width")] public int Width;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("origin")] public float[] Origin;
        [JsonProperty("size")] public float[] Size;
        [JsonProperty("heightMin")] public float HeightMin;
        [JsonProperty("heightMax")] public float HeightMax;
        [JsonProperty("coverage")] public float Coverage;

        /// <summary>
        /// Ground colours, one RGB triple per sample. This is the surface the
        /// player walks on — sand on the shore, snow above — taken from the
        /// material each ray landed on rather than invented.
        /// </summary>
        [JsonProperty("colorFile")] public string ColorFile;
    }

    public sealed class AlbedoDto
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("resolution")] public int Resolution;
        [JsonProperty("origin")] public float[] Origin;
        [JsonProperty("size")] public float[] Size;
    }

    /// <summary>
    /// The segment's real geometry: the triangles the game draws, in world
    /// space, coloured by the vertex data its terrain shaders read.
    /// Present alongside the heightfield, which is cheaper but cannot hold a
    /// cave or an overhang.
    /// </summary>
    public sealed class MeshDto
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("vertexCount")] public int VertexCount;
        [JsonProperty("triangleCount")] public int TriangleCount;
        [JsonProperty("meshCount")] public int MeshCount;
        [JsonProperty("hasColors")] public bool HasColors;
    }

    public sealed class MarkerDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("type")] public string Type;
        [JsonProperty("name")] public string Name;
        [JsonProperty("pos")] public float[] Pos;
    }
}
