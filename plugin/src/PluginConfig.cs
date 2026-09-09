using BepInEx.Configuration;

namespace PeakMapInteractive
{
    /// <summary>Which corner of the screen the GPS is pinned to.</summary>
    internal enum ScreenCorner
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

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
        public readonly ConfigEntry<ScreenCorner> MinimapCorner;
        public readonly ConfigEntry<float> MinimapMarginX;
        public readonly ConfigEntry<float> MinimapMarginY;
        public readonly ConfigEntry<string> MinimapPlayerColour;
        public readonly ConfigEntry<int> MinimapStartZoom;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapToggleKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapZoomInKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapZoomOutKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapAngleKey;
        public readonly ConfigEntry<float> MinimapCompassNeedleOffset;
        public readonly ConfigEntry<bool> MinimapIcons;
        public readonly ConfigEntry<float> MinimapMarkerSize;
        public readonly ConfigEntry<bool> MinimapDumpIcons;
        public readonly ConfigEntry<bool> MinimapAutoBakeIcons;
        public readonly ConfigEntry<UnityEngine.KeyCode> MinimapBakeAllKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> ScreenshotKey;
        public readonly ConfigEntry<float> MinimapStartDelay;
        public readonly ConfigEntry<int> MinimapIconResolution;

        public PluginConfig(ConfigFile cfg)
        {
            CaptureHotkey = cfg.Bind(
                "Automation", "CaptureHotkey", UnityEngine.KeyCode.None,
                "Exports the whole mountain to disk when pressed. Unbound by default: it " +
                "borrows the camera, hides scenery and writes hundreds of megabytes, none of " +
                "which anybody wants from a stray keypress mid-climb. Bind it to F9 to use it.");

            OutputDirectory = cfg.Bind(
                "Output", "Directory", "capture-output",
                "Where snapshots are written. Relative paths resolve against the PEAK install folder.");

            AutoRun = cfg.Bind(
                "Automation", "AutoRun", false,
                "Take the game over: go offline, start a solo run on its own, and capture or " +
                "photograph without anybody at the keyboard. Off by default and it must stay " +
                "that way — somebody who installs this mod to get a map is not expecting it " +
                "to seize their session and quit the game. For developing the mod, not for " +
                "playing it.");

            QuitWhenDone = cfg.Bind(
                "Automation", "QuitWhenDone", false,
                "Quit the game once the automated work is finished. Only ever does anything " +
                "under AutoRun, and off by default for the same reason AutoRun is.");

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
                "Mesh", "ExportMeshes", false,
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

            MinimapCorner = cfg.Bind(
                "Minimap", "Corner", ScreenCorner.TopRight,
                "Which corner of the screen the GPS hangs in. Dragging it with the mouse is " +
                "not offered on purpose: during a run the cursor belongs to the game, and " +
                "prising it away to move a window is a worse trade than picking a corner once.");

            MinimapMarginX = cfg.Bind(
                "Minimap", "MarginXPixels", 14f,
                new ConfigDescription("How far in from the side of the screen.",
                    new AcceptableValueRange<float>(0f, 600f)));

            MinimapMarginY = cfg.Bind(
                "Minimap", "MarginYPixels", 14f,
                new ConfigDescription("How far in from the top or bottom of the screen.",
                    new AcceptableValueRange<float>(0f, 600f)));

            MinimapPlayerColour = cfg.Bind(
                "Minimap", "PlayerMarkerColour", "#FF8C33",
                "The colour of the arrow that is you, as a hex value. Anything the game can " +
                "read works: #RRGGBB, or #RRGGBBAA to make it see-through.");

            MinimapStartZoom = cfg.Bind(
                "Minimap", "StartZoomStep", 3,
                new ConfigDescription(
                    "Which zoom step the map opens on, counting the tightest as 1. The steps " +
                    "run 20, 29, 41, 58, 83, 119, 170, 243, 347, 496, 708, 1012, 1446 and " +
                    "2000 metres across, and the zoom keys move one step at a time.",
                    new AcceptableValueRange<int>(1, 14)));

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

            ScreenshotKey = cfg.Bind(
                "Debug", "ScreenshotKey", UnityEngine.KeyCode.F11,
                "Saves a full-resolution screenshot to a 'shots' folder in the output " +
                "directory. Here because the pictures a mod page needs are taken while " +
                "playing, and stopping to fight the clipboard loses the moment.");

            MinimapBakeAllKey = cfg.Bind(
                "Minimap", "BakeAllKey", UnityEngine.KeyCode.F10,
                "Photographs every kind of thing anywhere on the loaded mountain at once, "  +
                "rather than waiting to walk past one of each. Meant for working on the "  +
                "icons: the nearest statue can be five minutes of climbing away.");

            MinimapAutoBakeIcons = cfg.Bind(
                "Minimap", "AutoBakeIcons", false,
                "With AutoRun on, walk the game into a solo run, photograph every marker " +
                "icon on the mountain and quit, with nobody at the keyboard. Replaces the " +
                "map capture for that run. For working on the icons: judging one only " +
                "needs the PNG, and making one only needs a loaded level.");

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
