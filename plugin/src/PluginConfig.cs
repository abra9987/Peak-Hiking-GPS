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

        public readonly ConfigEntry<bool> TrackerPreview;
        public readonly ConfigEntry<float> SoundVolume;
        public readonly ConfigEntry<bool> TrackerAsItem;
        public readonly ConfigEntry<Rarity> TrackerRarity;
        public readonly ConfigEntry<SpawnPool> TrackerSpawnPools;
        public readonly ConfigEntry<float> TrackerScale;
        public readonly ConfigEntry<float> TrackerReadoutScale;
        public readonly ConfigEntry<float> TrackerArrowScale;
        public readonly ConfigEntry<float> TrackerMarkerScale;
        public readonly ConfigEntry<float> TrackerHoldX;
        public readonly ConfigEntry<float> TrackerHoldY;
        public readonly ConfigEntry<float> TrackerHoldZ;
        public readonly ConfigEntry<float> TrackerTilt;
        public readonly ConfigEntry<float> TrackerLuggageLift;
        public readonly ConfigEntry<float> TrackerLuggageTurn;
        public readonly ConfigEntry<float> TrackerOffsetUp;
        public readonly ConfigEntry<float> TrackerOffsetAway;
        public readonly ConfigEntry<float> TrackerGripAcross;
        public readonly ConfigEntry<float> TrackerGripBehind;
        public readonly ConfigEntry<float> TrackerGripHeight;
        public readonly ConfigEntry<float> TrackerGripAngleX;
        public readonly ConfigEntry<float> TrackerGripAngleY;
        public readonly ConfigEntry<float> TrackerGripAngleZ;
        public readonly ConfigEntry<UnityEngine.KeyCode> ReloadConfigKey;
        public readonly ConfigEntry<UnityEngine.KeyCode> SpawnDeviceKey;
        public readonly ConfigEntry<string> SpawnItemName;

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
                "prising it away to move a window is a worse trade than picking a corner once. " +
                "Both top corners are clear of PEAK's own HUD. The bottom two are not — at the " +
                "default margin the GPS sits on the stamina bar on the left and on the item " +
                "slots on the right — so raise MarginYPixels to about 95 or 115 to lift it off.");

            MinimapMarginX = cfg.Bind(
                "Minimap", "MarginXPixels", 14f,
                new ConfigDescription("How far in from the side of the screen.",
                    new AcceptableValueRange<float>(0f, 600f)));

            MinimapMarginY = cfg.Bind(
                "Minimap", "MarginYPixels", 14f,
                new ConfigDescription(
                    "How far in from the top or bottom of the screen. This is the one to raise " +
                    "if a bottom corner puts the GPS over the stamina bar or the item slots.",
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
                "Debug", "ScreenshotKey", UnityEngine.KeyCode.None,
                "Saves a full-resolution screenshot to a 'shots' folder in the output " +
                "directory. Unbound, because it exists to photograph the map while working " +
                "on the mod, and a player already has Steam, the graphics driver and Windows " +
                "itself for screenshots — three keys that do it better than a fourth would. " +
                "Set a key here if you are working on the mod.");

            ReloadConfigKey = cfg.Bind(
                "Debug", "ReloadConfigKey", UnityEngine.KeyCode.None,
                "Re-reads this file while the game is running, so a setting can be tried " +
                "without restarting. Unbound: it exists for tuning the mod, not for " +
                "playing it. Only the settings that act on their own change take effect " +
                "at once — the grip points do; keys and sizes are read every frame anyway.");

            SpawnDeviceKey = cfg.Bind(
                "Debug", "SpawnDeviceKey", UnityEngine.KeyCode.None,
                "Puts a navigator straight into your hands, wherever you are — the airport " +
                "included, where the mirror shows how it is held. Unbound: it is for " +
                "working on the grip, and a player is meant to find the thing in a " +
                "suitcase. Needs PEAKLib, like the item itself.");

            SpawnItemName = cfg.Bind(
                "Debug", "SpawnItemName", "",
                "What SpawnDeviceKey hands over. Empty for the navigator; otherwise the " +
                "name of one of the game's own items, such as Compass or Passport, so " +
                "the two can be held one after the other in front of the airport mirror " +
                "and compared.");

            MinimapBakeAllKey = cfg.Bind(
                "Minimap", "BakeAllKey", UnityEngine.KeyCode.None,
                "Photographs every kind of thing anywhere on the loaded mountain at once, "  +
                "rather than waiting to walk past one of each. Unbound: it is meant for "  +
                "working on the icons, where the nearest statue can be five minutes of "  +
                "climbing away, and it is a lot of work to set off by leaning on a key "  +
                "mid-climb. Playing needs none of it — an icon is photographed the first "  +
                "time you see that kind of thing anyway.");

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

            SoundVolume = cfg.Bind(
                "Sound", "Volume", 0.7f,
                "How loud the device is: the chirp as it wakes, the click of its buttons, and " +
                "the dull knock when the zoom will go no further. Set to 0 for a silent map. " +
                "The clicks are deliberately quieter than the chirps, because a climb involves " +
                "a great many more of them.");

            TrackerAsItem = cfg.Bind(
                "Tracker", "AsItem", true,
                "Put the navigator in the world as a real item: found in luggage, carried in " +
                "both hands, droppable and shareable. Needs PEAKLib installed; with it absent " +
                "this does nothing and the map still works from the corner of the screen, " +
                "which is why the dependency is a soft one.");

            TrackerRarity = cfg.Bind(
                "Tracker", "Rarity", Rarity.Rare,
                "How often the navigator turns up in the luggage it can appear in. The game's " +
                "own scale, from Common to RidiculouslyRare. Rare by default: a map should be " +
                "a find rather than a fixture, and a party only needs one.");

            TrackerSpawnPools = cfg.Bind(
                "Tracker", "SpawnPools", SpawnPool.LuggageBeach,
                "Which luggage it can be found in, from the game's own list — several can be " +
                "combined with commas. The beach by default, because a map is worth most " +
                "before the climb rather than after it: a navigator found in the Citadel is a " +
                "souvenir.");

            TrackerScale = cfg.Bind(
                "Tracker", "Scale", 4f,
                new ConfigDescription(
                    "How large the navigator is, as a multiple of its real size. It is drawn at " +
                    "90 by 120 millimetres, which is what a handheld unit measures and is also " +
                    "small in a pair of hands — a map on it is legible, a line of numbers under " +
                    "the map less so. Raising this trades the honest scale for a screen you can " +
                    "read at a glance. The case, its collision and the grip points all scale " +
                    "together, so the hands keep hold of it. Held in the game it reads as far " +
                    "smaller than its measurements suggest, because PEAK's characters have " +
                    "enormous hands and every prop is drawn to match them rather than to " +
                    "scale. Four was settled by holding it.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            TrackerReadoutScale = cfg.Bind(
                "Tracker", "ReadoutScale", 1f,
                new ConfigDescription(
                    "How large the line of numbers under the map is, as a multiple. The strip " +
                    "and the lettering in it grow together, and both take the space from the " +
                    "map. Separate from Scale because they are different trades: one makes the " +
                    "whole device bigger in the world, this one gives the numbers more of a " +
                    "device that is already the size it should be.",
                    new AcceptableValueRange<float>(0.5f, 3f)));

            TrackerArrowScale = cfg.Bind(
                "Tracker", "ArrowScale", 3f,
                new ConfigDescription(
                    "How large the arrow showing where you are is on the device's screen, as a " +
                    "multiple. It was drawn for a map pinned to a corner of the screen at full " +
                    "resolution; on a device the same picture arrives much smaller, and the one " +
                    "marker anybody looks for first vanished into the terrain.",
                    new AcceptableValueRange<float>(1f, 8f)));

            // Where the game holds the item, relative to the head and in the
            // direction of the look: right, up and forward, in metres. This is
            // Item.defaultPos, which every one of the game's items sets in its
            // prefab and which a device built in code left at zero — so the
            // hold point sat inside the head, the arms folded back to reach it,
            // and on pick-up the game slid the device from 0.7 m in front into
            // the face over a fifth of a second, which read as the arms slowly
            // winding up. None of the grip angles could fix that, because
            // none of them was the cause.
            TrackerHoldX = cfg.Bind(
                "Tracker", "HoldX", 0f,
                new ConfigDescription(
                    "Where the device is held, to the right of the head, in metres.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            TrackerHoldY = cfg.Bind(
                "Tracker", "HoldY", -0.25f,
                new ConfigDescription(
                    "Where the device is held, above the head, in metres. Negative is below.",
                    new AcceptableValueRange<float>(-1.5f, 1f)));

            TrackerHoldZ = cfg.Bind(
                "Tracker", "HoldZ", 0.85f,
                new ConfigDescription(
                    "Where the device is held, in front of the head, in metres. Zero is " +
                    "inside the head, which is what an item gets when nobody says otherwise.",
                    new AcceptableValueRange<float>(0f, 1.5f)));

            TrackerLuggageLift = cfg.Bind(
                "Tracker", "LuggageLift", 0.12f,
                new ConfigDescription(
                    "How far above a suitcase's spawn point the device is laid, in metres, " +
                    "so it rests on the floor of the case rather than through it. The " +
                    "spawn point is a little above the floor and the device is thick.",
                    new AcceptableValueRange<float>(-0.2f, 0.4f)));

            TrackerLuggageTurn = cfg.Bind(
                "Tracker", "LuggageTurn", 180f,
                new ConfigDescription(
                    "How the device is turned about the vertical when laid in a suitcase, " +
                    "in degrees. 180 lays it across the case with the antenna towards the lid, away from whoever opened it.",
                    new AcceptableValueRange<float>(-180f, 180f)));

            TrackerTilt = cfg.Bind(
                "Tracker", "TiltDegrees", 10f,
                new ConfigDescription(
                    "How far the top of the device leans back towards the face, in degrees. " +
                    "Zero holds it square to the line of sight; a phone in real hands is " +
                    "tipped ten or fifteen degrees so the screen faces the eyes.",
                    new AcceptableValueRange<float>(-45f, 45f)));

            // Where the case sits relative to the point the game holds an item
            // at. Every item is held at the same spot in front of the chest;
            // what differs is where each one's model sits inside its own root.
            // The compass's mesh is 15 centimetres above its root, which is
            // why it rides above the hands rather than between them, and why
            // a device placed exactly at the root sat too close to the face
            // with the arms folded back into the body to reach it.
            TrackerOffsetUp = cfg.Bind(
                "Tracker", "OffsetUp", 0f,
                new ConfigDescription(
                    "How far above the held position the device sits, in metres at Scale " +
                    "1. The game's compass rides 0.15 above its own. Negative is below.",
                    new AcceptableValueRange<float>(-0.3f, 0.3f)));

            TrackerOffsetAway = cfg.Bind(
                "Tracker", "OffsetAway", 0f,
                new ConfigDescription(
                    "How far from the face the device sits, in metres at Scale 1, beyond " +
                    "where the game holds it. Positive pushes it away, negative pulls it in.",
                    new AcceptableValueRange<float>(-0.3f, 0.3f)));

            // Where the hands take hold, in the device's own metres before
            // Scale. Settings rather than constants because the only judge of a
            // grip is a person looking at one, and every pass so far was
            // reasoned from numbers and wrong. Changing any of these in a
            // running game moves the grip points at once; the game welds the
            // hands on at pick-up, so drop the device and take it again to see.
            TrackerGripAcross = cfg.Bind(
                "Tracker", "GripAcross", 0.05f,
                new ConfigDescription(
                    "How far each hand sits from the centre of the device, sideways, in " +
                    "metres at Scale 1. The case is 0.045 wide from the centre to its edge, " +
                    "so 0.045 puts the palms on its sides and more than that holds it from " +
                    "outside. Multiplied by Scale along with the case, so at Scale 3 a " +
                    "centimetre here moves a hand three.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            TrackerGripBehind = cfg.Bind(
                "Tracker", "GripBehind", -0.033f,
                new ConfigDescription(
                    "How far behind the middle of the case the hands sit, in metres at " +
                    "Scale 1. Positive is towards the back cover, negative towards the glass. " +
                    "Too far back and the hands meet behind the device holding nothing.",
                    new AcceptableValueRange<float>(-0.1f, 0.1f)));

            TrackerGripHeight = cfg.Bind(
                "Tracker", "GripHeight", -0.012f,
                new ConfigDescription(
                    "How far up the device the hands sit, in metres at Scale 1, from the " +
                    "model's origin. The case is 0.12 tall; low is how a handheld is held, " +
                    "with the thumbs near the buttons.",
                    new AcceptableValueRange<float>(-0.15f, 0.15f)));

            // The left hand's rotation in the item's frame, as the three Unity
            // Euler angles; the right hand is its mirror. The item's own frame
            // while held is +X to the right, +Y up and +Z away from the face.
            //
            // The defaults are the game's passport, held open in two hands in
            // front of the face: (270, 202, 0). Its bottles use (270, 195, 0)
            // and its compass (315, 161, 45). Three angles measured off a
            // screenshot never matched what a person saw, because the angles
            // were never the problem; what they are for now is a nudge.
            TrackerGripAngleX = cfg.Bind(
                "Tracker", "GripAngleX", 270f,
                new ConfigDescription(
                    "Left hand rotation about the item's sideways axis, in degrees. 270 is " +
                    "what every two-handed item in the game uses. The right hand mirrors it.",
                    new AcceptableValueRange<float>(0f, 360f)));

            TrackerGripAngleY = cfg.Bind(
                "Tracker", "GripAngleY", 202f,
                new ConfigDescription(
                    "Left hand rotation about the vertical, in degrees. 180 is two parallel " +
                    "hands; above it the fingers turn in towards each other. The passport " +
                    "uses 202, bottles 195. The right hand takes the opposite angle.",
                    new AcceptableValueRange<float>(0f, 360f)));

            TrackerGripAngleZ = cfg.Bind(
                "Tracker", "GripAngleZ", 0f,
                new ConfigDescription(
                    "Left hand rotation about the item's forward axis, in degrees: the roll " +
                    "of the wrist. Zero for the passport and the bottles, 45 for the compass. " +
                    "The right hand takes the opposite angle.",
                    new AcceptableValueRange<float>(0f, 360f)));

            TrackerMarkerScale = cfg.Bind(
                "Tracker", "MarkerScale", 2f,
                new ConfigDescription(
                    "How large the chest and creature markers are on the device's screen, as " +
                    "a multiple of Minimap/MarkerSizePixels. The same reason as ArrowScale: " +
                    "the markers were sized for a map in a corner at full resolution, and on " +
                    "a screen seen at a fraction of that the round plates shrank to dots.",
                    new AcceptableValueRange<float>(1f, 5f)));

            TrackerPreview = cfg.Bind(
                "Tracker", "PreviewModel", false,
                "With AutoRun on, stand the 3D device in front of the camera, photograph it " +
                "from four sides into a 'tracker' folder, and quit. Replaces the other " +
                "automated work for that run. For building the device: every way the model " +
                "can arrive wrong — inside out, wrongly coloured, facing backwards — is " +
                "invisible in the code and obvious in a photograph.");

            WriteDiagnostics = cfg.Bind(
                "Debug", "WriteDiagnostics", false,
                "Write an extra diagnostics.json listing every component type seen while " +
                "collecting markers. Useful when the game adds content the registry does not know.");
        }
    }
}
