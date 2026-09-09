using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeakMapInteractive
{
    /// <summary>
    /// Entry point. Owns configuration, the Harmony instance and the host
    /// GameObject that coroutines are driven from.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("PEAK.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.peakmapinteractive.capture";
        public const string Name = "Peak Map Interactive - Capture";
        public const string Version = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static PluginConfig Settings { get; private set; }

        /// <summary>Directory snapshots are written to.</summary>
        internal static string OutputDir { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Settings = new PluginConfig(Config);

            OutputDir = ResolveOutputDir(Settings.OutputDirectory.Value);
            Directory.CreateDirectory(OutputDir);

            // Patched only when the automation is actually wanted.
            //
            // Everything the mod does for a player it does by reading and
            // drawing: a camera of its own, a canvas of its own, and nothing
            // written back. The one exception is the automation, which patches
            // the main menu and the loading routine to drive the game itself —
            // so with it off, nothing of PEAK's is touched at all. That matters
            // most in company: a mod that installs no patches cannot break
            // somebody else's session, and cannot be blamed for it either.
            if (Settings.AutoRun.Value)
            {
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogWarning("AutoRun is on: the game will be driven automatically.");
            }

            if (Settings.AutoRun.Value && Settings.QuietCapture.Value)
            {
                // A scheduled capture should not announce itself. Running in
                // the background matters as much as the silence: Unity stops
                // updating an unfocused player otherwise, which would stall the
                // capture the moment the window loses focus.
                Application.runInBackground = true;
                AudioListener.volume = 0f;
                AudioListener.pause = true;
                Logger.LogInfo("Quiet capture: audio muted, running in background.");
            }

            // Never both at once. A capture drives the game itself — it forces
            // the loading screen away mid-spawn, borrows the camera and quits
            // when finished — and running that under someone who is playing
            // leaves the character half-initialised and stuck under the map.
            if (Settings.AutoRun.Value && Settings.MinimapEnabled.Value)
            {
                Logger.LogWarning(
                    "AutoRun is on, so the minimap stays off: automation interrupts the " +
                    "character's spawn and is not safe to play under. Icons are still " +
                    "photographed if AutoBakeIcons is set — that needs the loaded level, " +
                    "not a working character.");
            }

            if (Settings.MinimapEnabled.Value && !Settings.AutoRun.Value)
            {
                gameObject.AddComponent<Minimap.MinimapController>();
                Logger.LogInfo($"Minimap enabled (toggle: {Settings.MinimapToggleKey.Value}).");
            }

            Logger.LogInfo($"{Name} {Version} loaded.");
            Logger.LogInfo($"Output directory: {OutputDir}");
            Logger.LogInfo($"Automation: autoRun={Settings.AutoRun.Value}, quitWhenDone={Settings.QuitWhenDone.Value}");
        }

        /// <summary>
        /// Watches for the map becoming available, and for the manual hotkey.
        ///
        /// Polling here rather than patching some game method that happens to
        /// run once the map exists: the readiness condition is explicit and
        /// visible, and it does not break when the game reorganises its
        /// startup path.
        /// </summary>
        private void Update()
        {
            // The game restores audio settings on scene loads, so silence has
            // to be re-asserted rather than set once.
            if (Settings.AutoRun.Value && Settings.QuietCapture.Value && AudioListener.volume != 0f)
            {
                AudioListener.volume = 0f;
            }

            if (Pipeline.CaptureRunner.IsRunning) return;

            // Must run before the world is drawn: once a mesh is uploaded, its
            // index buffer can no longer be made readable, and the terrain
            // shells are exactly the meshes that would be lost.
            if (Settings.ExportMeshes.Value && !Pipeline.CaptureRunner.HasCompleted)
                Capture.MeshPrimer.Tick();

            if (Input.GetKeyDown(Settings.ScreenshotKey.Value)) Snapshot();

            if (Input.GetKeyDown(Settings.CaptureHotkey.Value))
            {
                Logger.LogInfo("Capture hotkey pressed.");
                Run(Pipeline.CaptureRunner.Run());
                return;
            }

            if (!Settings.AutoRun.Value) return;
            if (!IsMapReady()) return;

            // Two things worth doing unattended once a run has loaded, and only
            // ever one of them: a full capture of the mountain, or a sweep of
            // every marker icon on it.
            if (Settings.MinimapAutoBakeIcons.Value)
            {
                if (Automation.IconRun.HasCompleted) return;

                Logger.LogInfo("AutoRun: map is ready, photographing icons.");
                Run(Automation.IconRun.Run());
                return;
            }

            if (Pipeline.CaptureRunner.HasCompleted) return;

            Logger.LogInfo("AutoRun: map is ready, starting capture.");
            Run(Pipeline.CaptureRunner.Run());
        }

        /// <summary>
        /// True once the run is actually being played, not merely loaded.
        ///
        /// The loading-screen check is not politeness, it is the difference
        /// between a photograph and a black square: while the loading screen
        /// is up the world is not drawn at all, so every capture route —
        /// render texture, render request, back buffer — comes back empty.
        /// </summary>
        private static bool IsMapReady()
        {
            if (LoadingScreenHandler.loading) return false;

            Character player = Character.localCharacter;
            if (player == null) return false;

            // The run opens with the character lying on the beach, eyes
            // closed, and the eyelid effect washes the screen white until it
            // finishes. Photographing through it produced a picture of the
            // inside of an eyelid rather than a mountain.
            if (player.data == null) return false;
            if (player.data.passedOut || player.data.fullyPassedOut) return false;
            if (player.data.passedOutOnTheBeach > 0f) return false;

            var handler = Zorro.Core.Singleton<MapHandler>.Instance;
            return handler?.segments != null
                   && handler.segments.Length > 0
                   && handler.segments[0]?.segmentParent != null;
        }

        /// <summary>
        /// Saves the screen as it stands, named for the moment it was taken.
        ///
        /// The screenshots a mod page needs are the ones taken mid-climb, and
        /// they are lost by stopping to fight with the clipboard. A key and a
        /// folder is the whole feature.
        /// </summary>
        private static void Snapshot()
        {
            try
            {
                string folder = Path.Combine(OutputDir, "shots");
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, $"peak-{System.DateTime.Now:yyyyMMdd-HHmmss}.png");
                ScreenCapture.CaptureScreenshot(path);

                Logger.LogInfo($"Screenshot: {path}");
            }
            catch (System.Exception error)
            {
                Logger.LogWarning($"Screenshot failed: {error.Message}");
            }
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); }
            catch { /* shutting down anyway */ }
        }

        /// <summary>
        /// Relative paths resolve against the game root so that a scheduled
        /// capture writes somewhere predictable regardless of working directory.
        /// </summary>
        private static string ResolveOutputDir(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured))
                return configured;

            string gameRoot = Path.GetDirectoryName(Application.dataPath);
            string relative = string.IsNullOrWhiteSpace(configured) ? "capture-output" : configured;
            return Path.GetFullPath(Path.Combine(gameRoot, relative));
        }

        /// <summary>Starts a coroutine on the plugin's own behaviour.</summary>
        internal static Coroutine Run(System.Collections.IEnumerator routine)
            => Instance.StartCoroutine(routine);
    }
}
