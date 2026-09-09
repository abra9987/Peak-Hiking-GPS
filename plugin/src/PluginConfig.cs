using BepInEx.Configuration;

namespace PeakMapInteractive
{
    /// <summary>
    /// All tunables live here so a scheduled capture can be reconfigured by
    /// editing the BepInEx config file, without a rebuild.
    /// </summary>
    internal sealed class PluginConfig
    {
        public readonly ConfigEntry<string> OutputDirectory;
        public readonly ConfigEntry<bool> AutoRun;
        public readonly ConfigEntry<bool> QuitWhenDone;
        public readonly ConfigEntry<bool> QuietCapture;
        public readonly ConfigEntry<int> HeightResolution;
        public readonly ConfigEntry<int> AlbedoResolution;
        public readonly ConfigEntry<float> BoundsPadding;
        public readonly ConfigEntry<bool> WriteDiagnostics;
        public readonly ConfigEntry<bool> ExportMeshes;
        public readonly ConfigEntry<float> MeshMinSize;
        public readonly ConfigEntry<int> MeshTriangleBudget;
        public readonly ConfigEntry<int> MeshLodLevel;
        public readonly ConfigEntry<int> MeshOnlySegment;
        public readonly ConfigEntry<bool> MeshIncludeFoliage;
        public readonly ConfigEntry<UnityEngine.KeyCode> CaptureHotkey;

        public readonly ConfigEntry<bool> MinimapEnabled;
        public readonly ConfigEntry<float> MinimapSize;
        public readonly ConfigEntry<float> MinimapSpan;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapToggleKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapZoomInKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapZoomOutKey;

        public PluginConfig(ConfigFile cfg)
        {
            CaptureHotkey = cfg.Bind(
                "Automation", "CaptureHotkey", UnityEngine.KeyCode.F9,
                "Captures the current map when pressed. Works whether or not AutoRun is on.");

            OutputDirectory = cfg.Bind(
                "Output", "Directory", "capture-output",
                "Where snapshots are written. Relative paths resolve against the PEAK install folder.");

            AutoRun = cfg.Bind(
                "Automation", "AutoRun", true,
                "Automatically go offline, start a solo run and capture without any input. " +
                "Turn off to capture manually via the Capture hotkey.");

            QuitWhenDone = cfg.Bind(
                "Automation", "QuitWhenDone", true,
                "Quit the game once a snapshot is written. Required for scheduled captures.");

            QuietCapture = cfg.Bind(
                "Automation", "QuietCapture", true,
                "During an automatic capture, mute the game and keep it running while unfocused, " +
                "so a scheduled run does not interrupt whatever you are doing.");

            HeightResolution = cfg.Bind(
                "Capture", "HeightResolution", 1024,
                new ConfigDescription(
                    "Heightfield samples per axis. 1024 gives roughly one sample per metre " +
                    "on a typical segment and costs ~2 MB per segment.",
                    new AcceptableValueRange<int>(128, 4096)));

            AlbedoResolution = cfg.Bind(
                "Capture", "AlbedoResolution", 4096,
                new ConfigDescription(
                    "Orthophoto resolution per axis.",
                    new AcceptableValueRange<int>(512, 8192)));

            BoundsPadding = cfg.Bind(
                "Capture", "BoundsPadding", 25f,
                "Extra world units added around a segment's computed bounds, so geometry " +
                "at the very edge is not clipped.");

            ExportMeshes = cfg.Bind(
                "Mesh", "ExportMeshes", true,
                "Export the segment's real geometry, so caves and overhangs survive. " +
                "A heightfield cannot represent them at all.");

            MeshMinSize = cfg.Bind(
                "Mesh", "MinSize", 10f,
                "Smallest world-space extent a loose mesh must have to be exported. " +
                "Drops thousands of pebbles and keeps terrain and landmarks.");

            MeshTriangleBudget = cfg.Bind(
                "Mesh", "TriangleBudget", 2000000,
                "Triangles per segment. Largest objects are exported first, so a tight " +
                "budget still yields the landmarks.");

            MeshLodLevel = cfg.Bind(
                "Mesh", "LodLevel", 0,
                "Which LOD to export: 0 is the detail the player sees up close. " +
                "The coarsest level is what a game draws at a distance and looks like " +
                "cheap low-poly when a map lets you zoom in on it.");

            MeshOnlySegment = cfg.Bind(
                "Mesh", "OnlySegment", -1,
                "Export geometry for this segment only (-1 for all). Set it while dialling " +
                "one biome in: a full capture is six times the wait per iteration.");

            MeshIncludeFoliage = cfg.Bind(
                "Mesh", "IncludeFoliage", false,
                "Include grass, vines and leaves. They are most of the triangle count and " +
                "little of the map, but a faithful reproduction needs them.");

            MinimapEnabled = cfg.Bind(
                "Minimap", "Enabled", true,
                "Show a live top-down view of the mountain in a corner of the screen, for " +
                "seeing where you are and planning where to climb next.");

            MinimapSize = cfg.Bind(
                "Minimap", "SizePixels", 320f,
                new ConfigDescription("On-screen size of the map window.",
                    new AcceptableValueRange<float>(120f, 900f)));

            MinimapSpan = cfg.Bind(
                "Minimap", "SpanMeters", 220f,
                new ConfigDescription("How many metres across the window covers.",
                    new AcceptableValueRange<float>(20f, 2000f)));

            MinimapToggleKey = cfg.Bind("Minimap", "ToggleKey", UnityEngine.KeyCode.M, "Shows or hides the map.");
            MinimapZoomInKey = cfg.Bind("Minimap", "ZoomInKey", UnityEngine.KeyCode.Equals, "Zooms in.");
            MinimapZoomOutKey = cfg.Bind("Minimap", "ZoomOutKey", UnityEngine.KeyCode.Minus, "Zooms out.");

            WriteDiagnostics = cfg.Bind(
                "Debug", "WriteDiagnostics", false,
                "Write an extra diagnostics.json listing every component type seen while " +
                "collecting markers. Useful when the game adds content the registry does not know.");
        }
    }
}
