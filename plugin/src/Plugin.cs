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

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

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

            if (Input.GetKeyDown(Settings.CaptureHotkey.Value))
            {
                Logger.LogInfo("Capture hotkey pressed.");
                Run(Pipeline.CaptureRunner.Run());
                return;
            }

            if (!Settings.AutoRun.Value || Pipeline.CaptureRunner.HasCompleted) return;
            if (!IsMapReady()) return;

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
            if (Character.localCharacter == null) return false;

            var handler = Zorro.Core.Singleton<MapHandler>.Instance;
            return handler?.segments != null
                   && handler.segments.Length > 0
                   && handler.segments[0]?.segmentParent != null;
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
