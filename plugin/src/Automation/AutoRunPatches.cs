using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// Walks the game from launch to a loaded solo run without any input, so a
    /// capture can be driven by a scheduled task.
    ///
    /// All of it is gated on the AutoRun setting: with automation off the
    /// plugin is inert and the game plays normally, which is what makes it safe
    /// to leave installed.
    /// </summary>
    [HarmonyPatch]
    internal static class AutoRunPatches
    {
        private static bool _kioskTriggered;

        private static bool Enabled => Plugin.Settings != null && Plugin.Settings.AutoRun.Value;

        /// <summary>Main menu reached: drop the session offline and load the airport.</summary>
        [HarmonyPatch(typeof(MainMenu), "Start")]
        [HarmonyPostfix]
        private static void MainMenuStarted()
        {
            if (!Enabled) return;

            Plugin.Logger.LogInfo("AutoRun: main menu reached, going offline.");
            RetrievableResourceSingleton<LoadingScreenHandler>.Instance.Load(
                LoadingScreen.LoadingScreenType.Basic, null, GoOfflineAndLoadAirport());
        }

        private static IEnumerator GoOfflineAndLoadAirport()
        {
            yield return MainMenu.DisconnectForOfflineMode();
            yield return RetrievableResourceSingleton<LoadingScreenHandler>.Instance
                .LoadSceneProcess("Airport", networked: false, yieldForCharacterSpawn: true);
        }

        /// <summary>
        /// Airport reached: check in and start the run. The passport spawning is
        /// the reliable signal that the scene is interactive; polling for the
        /// kiosk alone can fire while the level is still assembling.
        /// </summary>
        [HarmonyPatch(typeof(LoadingScreenHandler), "LoadingRoutine")]
        [HarmonyPostfix]
        private static void WrapLoadingRoutine(ref IEnumerator __result)
        {
            if (!Enabled) return;
            __result = StartRunWhenAirportReady(__result);
        }

        private static IEnumerator StartRunWhenAirportReady(IEnumerator original)
        {
            while (original.MoveNext())
                yield return original.Current;

            if (_kioskTriggered) yield break;

            var passport = Object
                .FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name.StartsWith("Passport"));

            if (passport == null) yield break;

            var kiosk = Object.FindFirstObjectByType<AirportCheckInKiosk>(FindObjectsInactive.Include);
            if (kiosk == null)
            {
                Plugin.Logger.LogWarning("AutoRun: passport found but no check-in kiosk.");
                yield break;
            }

            _kioskTriggered = true;
            Plugin.Logger.LogInfo("AutoRun: airport ready, starting run.");
            kiosk.StartGame(0);
        }
    }
}
