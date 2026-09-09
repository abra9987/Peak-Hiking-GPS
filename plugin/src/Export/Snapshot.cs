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
        [JsonProperty("capture")] public CaptureSettings Capture = new CaptureSettings();
        [JsonProperty("segments")] public List<SegmentDto> Segments = new List<SegmentDto>();
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
    }

    public sealed class AlbedoDto
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("resolution")] public int Resolution;
        [JsonProperty("origin")] public float[] Origin;
        [JsonProperty("size")] public float[] Size;
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
