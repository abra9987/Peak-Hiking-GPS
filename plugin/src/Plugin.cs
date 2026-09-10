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
        public const string Guid = "com.abra9987.hikinggps";
        public const string Name = "Hiking GPS";

        /// <summary>
        /// Taken from the project file rather than written here.
        ///
        /// It used to be a literal, and it silently fell a version behind the
        /// moment the project file moved: the packaging script reads the project
        /// file, so a zip called 1.0.1 shipped an assembly announcing itself as
        /// 1.0.0. The generated constant comes from the same &lt;Version&gt; the
        /// script reads, so there is now one number instead of two that agree
        /// only as long as somebody remembers both.
        ///
        /// Only the version is taken from it. The generated GUID and name are
        /// derived from the assembly name and are not what this plugin calls
        /// itself.
        /// </summary>
        public const string Version = MyPluginInfo.PLUGIN_VERSION;

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

            // The map is built later when the device might exist.
            //
            // Where it goes depends on whether the navigator turns out to be a
            // real item — on its screen if it is, in the corner of the screen if
            // it is not — and that is not knowable this early: PEAKLib loads
            // after this plugin does. So the decision waits until the moment the
            // item is either registered or found to be impossible, which is a
            // few frames later and before anything is drawn.
            if (Settings.MinimapEnabled.Value && !Settings.AutoRun.Value && !Settings.TrackerAsItem.Value)
            {
                gameObject.AddComponent<Minimap.MinimapController>();
                Logger.LogInfo($"Minimap enabled (toggle: {Settings.MinimapToggleKey.Value}).");
            }

            Logger.LogInfo($"{Name} {Version} loaded.");
            Logger.LogInfo($"Output directory: {OutputDir}");
            Logger.LogInfo($"Automation: autoRun={Settings.AutoRun.Value}, quitWhenDone={Settings.QuitWhenDone.Value}");
        }

        private float _nextHeldLog;

        /// <summary>
        /// What the local character is holding and where its hands are on
        /// it, every two seconds. Only while the spawn key is bound, so a
        /// player's log stays quiet. The point is to hold the game's compass
        /// and then the device, and read off what differs between them.
        /// </summary>
        private void LogHeldItem()
        {
            if (Time.unscaledTime < _nextHeldLog) return;
            _nextHeldLog = Time.unscaledTime + 2f;

            Character character = Character.localCharacter;
            Item held = character == null || character.data == null ? null : character.data.currentItem;
            if (held == null) return;

            Transform l = held.transform.Find("Hand_L");
            Transform r = held.transform.Find("Hand_R");
            Rigidbody rig = held.GetComponent<Rigidbody>();
            var refs = character.refs;

            Logger.LogInfo(
                $"Held: '{held.name}' mass {held.mass} rigMass {(rig == null ? -1f : rig.mass)} " +
                $"inertia {(rig == null ? Vector3.zero : rig.inertiaTensor).ToString("F4")} " +
                $"com {(rig == null ? Vector3.zero : rig.centerOfMass).ToString("F3")} " +
                $"scale {held.transform.lossyScale.ToString("F2")} forceScale {held.forceScale} " +
                $"defaultPos {held.defaultPos.ToString("F3")} defaultForward {held.defaultForward.ToString("F2")} | " +
                $"  Hand_L {(l == null ? "missing" : l.localPosition.ToString("F3") + " euler " + l.localEulerAngles.ToString("F1"))} | " +
                $"  Hand_R {(r == null ? "missing" : r.localPosition.ToString("F3") + " euler " + r.localEulerAngles.ToString("F1"))} | " +
                $"  item at {held.transform.position.ToString("F2")} fwd {held.transform.forward.ToString("F2")} " +
                $"anim item at {refs.animationItemTransform.position.ToString("F2")} fwd {refs.animationItemTransform.forward.ToString("F2")} | " +
                $"  IK L {refs.IKHandTargetLeft.position.ToString("F2")} / {refs.IKHandTargetLeft.rotation.eulerAngles.ToString("F0")} " + " | " +
                $"  IK R {refs.IKHandTargetRight.position.ToString("F2")} / {refs.IKHandTargetRight.rotation.eulerAngles.ToString("F0")} ");
        }

        /// <summary>
        /// A navigator in the local character's hands, the way the game's own
        /// debug command hands out items: made through Photon so it has a
        /// view, then picked up through <c>Item.Interact</c> so the hands are
        /// welded on exactly as they would be for one found in a suitcase.
        /// </summary>
        private void SpawnDeviceInHand()
        {
            Character character = Character.localCharacter;
            if (character == null)
            {
                Logger.LogWarning("Spawn: there is no local character yet.");
                return;
            }

            string other = Settings.SpawnItemName.Value?.Trim();
            GameObject spawned;

            if (string.IsNullOrEmpty(other))
            {
                if (!Tracker.TrackerItem.Registered)
                {
                    Logger.LogWarning("Spawn: the item is not registered; is PEAKLib installed?");
                    return;
                }
                spawned = Automation.HandShot.Spawn(character);
            }
            else
            {
                // One of the game's own, by the same path its debug command uses.
                try
                {
                    spawned = Photon.Pun.PhotonNetwork.Instantiate(
                        "0_Items/" + other, character.Center + Vector3.up * 0.5f, Quaternion.identity, 0);
                }
                catch (System.Exception error)
                {
                    Logger.LogWarning($"Spawn: could not spawn '{other}': {error.Message}");
                    return;
                }
            }

            Item item = spawned == null ? null : spawned.GetComponent<Item>();
            if (item == null) return;

            item.Interact(character);
            Logger.LogInfo($"Spawn: '{item.name}' handed over.");
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

            TryRegisterItem();

            if (Input.GetKeyDown(Settings.ScreenshotKey.Value)) Snapshot();

            // Re-read the config file. BepInEx does not watch it, and rewrites
            // it from memory on exit, so this is the only way an edit made
            // while the game runs reaches the game rather than being lost.
            if (Input.GetKeyDown(Settings.ReloadConfigKey.Value))
            {
                Config.Reload();
                Logger.LogInfo("Config reloaded from disk.");
            }

            if (Input.GetKeyDown(Settings.SpawnDeviceKey.Value)) SpawnDeviceInHand();

            if (Settings.SpawnDeviceKey.Value != KeyCode.None) LogHeldItem();

            if (Input.GetKeyDown(Settings.CaptureHotkey.Value))
            {
                Logger.LogInfo("Capture hotkey pressed.");
                Run(Pipeline.CaptureRunner.Run());
                return;
            }

            if (!Settings.AutoRun.Value) return;
            if (!IsMapReady()) return;

            // Three things worth doing unattended once a run has loaded, and
            // only ever one of them: a full capture of the mountain, a sweep of
            // every marker icon on it, or a look at the device itself.
            if (Settings.TrackerPreview.Value)
            {
                if (Automation.TrackerRun.HasCompleted) return;

                Logger.LogInfo("AutoRun: map is ready, photographing the device.");
                Run(Automation.TrackerRun.Run());
                return;
            }

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

        /// <summary>The plugin id of PEAKLib's items module, read off its own assembly.</summary>
        private const string PeakLibItems = "com.github.PEAKModding.PEAKLib.Items";

        /// <summary>
        /// Whether the library the physical item needs is installed.
        ///
        /// Asked of BepInEx rather than by touching anything of PEAKLib's, so
        /// that the answer can be "no" without the question itself failing.
        /// </summary>
        internal static bool HasPeakLib
            => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(PeakLibItems);

        private bool _itemTried;
        private bool _itemInDatabase;

        /// <summary>
        /// Registers the navigator as a real item, once, as soon as it can be.
        ///
        /// Not in Awake. The device is built with a material, the material
        /// wants one of the game's own shaders, and at the moment a BepInEx
        /// plugin wakes up the game has barely started loading its own content
        /// — Shader.Find would come back empty and the device would be drawn in
        /// magenta for the rest of the session. Waiting for the shader to exist
        /// is waiting for the game to be ready, stated as a thing that can be
        /// checked rather than as a delay somebody tuned.
        ///
        /// Late is safe: PEAKLib adds an item to the database whenever it is
        /// handed one, whether or not the database has already loaded.
        /// </summary>
        private void TryRegisterItem()
        {
            if (!Settings.TrackerAsItem.Value) return;

            // Checked every frame until it is true, because the item database
            // may load long after the item is registered — and because the hook
            // that would normally do this belongs to a library that can fail to
            // install without saying so. Two dictionary lookups a frame is a
            // cheap price for not silently shipping an item nobody can find.
            if (_itemTried && !_itemInDatabase && HasPeakLib)
            {
                try { _itemInDatabase = Tracker.TrackerRegistration.EnsureInDatabase(); }
                catch (System.Exception error)
                {
                    _itemInDatabase = true;   // do not try again every frame forever
                    Logger.LogError($"The navigator could not be added to the item database: {error}");
                }
            }

            if (_itemTried) return;
            if (Shader.Find("W/Peak_Standard") == null) return;

            _itemTried = true;

            if (!HasPeakLib)
            {
                Logger.LogInfo(
                    "PEAKLib is not installed, so the navigator stays a map in the corner " +
                    "rather than an item you can pick up. Nothing else is affected.");

                BuildMap(onDevice: false);
                return;
            }

            bool registered = false;

            try
            {
                registered = Tracker.TrackerRegistration.Register();
                if (!registered) Logger.LogWarning("The navigator could not be registered as an item.");
            }
            catch (System.Exception error)
            {
                Logger.LogError($"The navigator could not be registered as an item: {error}");
            }

            BuildMap(onDevice: registered);
        }

        /// <summary>
        /// Builds the map, once it is known where it is going to be shown.
        ///
        /// On the device when there is a device: a player holding a navigator
        /// does not also want a second one floating in the corner of the screen,
        /// and rendering the mountain twice to give them one would be a strange
        /// way to spend a frame. In the corner when the item could not be made,
        /// because a map somewhere is much better than no map at all.
        /// </summary>
        private void BuildMap(bool onDevice)
        {
            if (!Settings.MinimapEnabled.Value || Settings.AutoRun.Value) return;
            if (gameObject.GetComponent<Minimap.MinimapController>() != null) return;

            Minimap.MinimapController.OnDevice = onDevice;
            gameObject.AddComponent<Minimap.MinimapController>();

            if (onDevice) Tracker.TrackerObject.ShowMap(Minimap.MinimapController.DeviceScreen);

            Logger.LogInfo(
                onDevice
                    ? "The map is on the navigator's screen; find one in the luggage on the beach."
                    : $"Minimap enabled (toggle: {Settings.MinimapToggleKey.Value}).");
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
