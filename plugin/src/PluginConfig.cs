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
        public readonly ConfigEntry<UnityEngine.KeyCode> CaptureHotkey;

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

            WriteDiagnostics = cfg.Bind(
                "Debug", "WriteDiagnostics", false,
                "Write an extra diagnostics.json listing every component type seen while " +
                "collecting markers. Useful when the game adds content the registry does not know.");
        }
    }
}
