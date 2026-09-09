using System.Collections;
using UnityEngine;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// Starts the game, walks it into a run, photographs every icon on the
    /// mountain and quits — with nobody at the keyboard.
    ///
    /// Dialling the icons in was costing a person five minutes of climbing per
    /// attempt, to reach one statue. Everything needed to judge an icon is a
    /// PNG on disk, and everything needed to produce one is a loaded level, so
    /// the person in that loop was only ever there to walk.
    ///
    /// This rides the same automation the capture uses to reach a solo run, and
    /// then does the one other thing worth doing once you are standing in a
    /// loaded world.
    /// </summary>
    internal static class IconRun
    {
        internal static bool HasCompleted { get; private set; }

        private const float Patience = 300f;

        internal static IEnumerator Run()
        {
            HasCompleted = true;

            // Nothing renders while the loading screen is up — not a render
            // texture, not a purpose-built camera, not the game's own. Every
            // photograph would come back black, silently.
            try
            {
                LoadingScreenHandler.KillCurrentLoadingScreen();
                Plugin.Logger.LogInfo("Icon run: dismissed the loading screen.");
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Icon run: could not dismiss the loading screen: {error.Message}");
            }

            // The same wait a player gets. The world is still being assembled
            // for a few seconds after it says it has loaded, and an icon taken
            // out of a half-built world is wrong in ways that are hard to spot
            // and impossible to correct later.
            yield return new WaitForSecondsRealtime(Plugin.Settings.MinimapStartDelay.Value);

            yield return Wake();

            Minimap.MinimapController.BakeEverything();

            float deadline = Time.realtimeSinceStartup + Patience;

            while (Minimap.IconBaker.Busy && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (Minimap.IconBaker.Busy)
                Plugin.Logger.LogWarning($"Icon run: still working after {Patience:0}s; stopping anyway.");

            Plugin.Logger.LogInfo("Icon run: finished.");

            if (!Plugin.Settings.QuitWhenDone.Value) yield break;

            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        /// <summary>
        /// Brings the whole mountain into the scene, so that there is something
        /// to photograph everywhere on it.
        ///
        /// A first sweep produced three icons out of hundreds of candidates,
        /// all of them from the beach. Everything further up exists as an
        /// object with a name, and every renderer under it is inactive until a
        /// player gets close enough for the segment to stream in. Nobody is
        /// going to get close during an unattended run, so the segments are
        /// switched on instead — the same thing the map capture does, and for
        /// the same reason.
        ///
        /// Only ever done in this mode. Waking six biomes at once is fine for
        /// a run that exists to take photographs and then quit, and is not
        /// something to do underneath somebody who is playing.
        /// </summary>
        private static IEnumerator Wake()
        {
            MapHandler map = Zorro.Core.Singleton<MapHandler>.Instance;
            if (map?.segments == null) yield break;

            int woken = 0;

            foreach (var segment in map.segments)
            {
                GameObject root = segment?.segmentParent;
                if (root == null) continue;

                if (!root.activeSelf)
                {
                    root.SetActive(true);
                    woken++;
                }

                // Sub-steps are the streaming units inside a segment; without
                // them the far half of a biome is still not in the scene.
                foreach (var substep in root.GetComponentsInChildren<EnablingSubstep>(includeInactive: true))
                {
                    if (substep == null || substep.gameObject.activeSelf) continue;

                    substep.gameObject.SetActive(true);
                    woken++;
                }

                if (segment.segmentCampfire != null && !segment.segmentCampfire.activeSelf)
                {
                    segment.segmentCampfire.SetActive(true);
                    woken++;
                }
            }

            Plugin.Logger.LogInfo($"Icon run: woke {woken} part(s) of the mountain.");

            // Waking something runs its Awake, and a chest only joins
            // ALL_LUGGAGE once that has happened.
            yield return new WaitForSecondsRealtime(3f);
        }
    }
}
