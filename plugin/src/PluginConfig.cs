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
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapAngleKey;
        public readonly ConfigEntry<float> MinimapCompassNeedleOffset;
        public readonly ConfigEntry<bool> MinimapIcons;
        public readonly ConfigEntry<float> MinimapMarkerSize;
        public readonly ConfigEntry<bool> MinimapDumpIcons;
        public readonly ConfigEntry<float> MinimapStartDelay;
        public readonly ConfigEntry<int> MinimapIconResolution;

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

            MinimapAngleKey = cfg.Bind(
                "Minimap", "AngleKey", UnityEngine.KeyCode.N,
                "Cycles the viewing angle: straight down, 75 degrees, 45 degrees. " +
                "A tilted view shows how much climbing lies between you and somewhere, " +
                "which looking straight down flattens away.");

            MinimapCompassNeedleOffset = cfg.Bind(
                "Minimap", "CompassNeedleOffset", 45f,
                new ConfigDescription(
                    "Which way the needle already points in the compass artwork, in degrees " +
                    "clockwise from up. The whole icon is rotated to aim that needle, so this " +
                    "is what stops it pointing off by a fixed amount.",
                    new AcceptableValueRange<float>(-180f, 180f)));

            MinimapIcons = cfg.Bind(
                "Minimap", "Icons", true,
                "Draw markers as pictures of the thing rather than as coloured dots. The " +
                "picture is photographed from the model already loaded in the game, once per " +
                "kind of thing, so nothing of PEAK's is copied and nothing is shipped. " +
                "Turn off to go back to plain markers.");

            MinimapMarkerSize = cfg.Bind(
                "Minimap", "MarkerSizePixels", 26f,
                new ConfigDescription(
                    "How large a marker is drawn. Markers still grow and shrink around this " +
                    "figure with how far above or below you the thing sits. Large enough that " +
                    "the picture inside is recognisable is the whole constraint.",
                    new AcceptableValueRange<float>(8f, 48f)));

            MinimapStartDelay = cfg.Bind(
                "Minimap", "StartDelaySeconds", 10f,
                new ConfigDescription(
                    "How long after the mountain finishes loading before the map opens and " +
                    "icons are photographed. The run begins with the character lying on the " +
                    "beach with their eyes shut while the world is still being assembled, and " +
                    "an icon is baked once and kept, so one taken too early stays wrong for " +
                    "the rest of the run.",
                    new AcceptableValueRange<float>(0f, 120f)));

            MinimapIconResolution = cfg.Bind(
                "Minimap", "IconResolutionPixels", 384,
                new ConfigDescription(
                    "How many pixels across each marker icon is photographed at, before it is " +
                    "trimmed to the object. The marker itself is drawn far smaller, so this " +
                    "buys detail in the mipmaps rather than on screen — which is what stops a " +
                    "suitcase turning to mush at map size.",
                    new AcceptableValueRange<int>(64, 1024)));

            MinimapDumpIcons = cfg.Bind(
                "Debug", "DumpIcons", false,
                "Write every baked marker icon to an 'icons' folder in the output directory, " +
                "as a PNG. The only way to judge whether an icon reads as what it is without " +
                "squinting at it twenty pixels across in the corner of the screen.");

            WriteDiagnostics = cfg.Bind(
                "Debug", "WriteDiagnostics", false,
                "Write an extra diagnostics.json listing every component type seen while " +
                "collecting markers. Useful when the game adds content the registry does not know.");
        }
    }
}
