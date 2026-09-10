using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// A photograph of the device lying in a suitcase, without anybody
    /// walking the beach to find one.
    ///
    /// Placing the device in luggage is the game's own code — spawn spot,
    /// visual centring, the item's luggage offsets, kinematic — and then a
    /// slide to the middle of the case by <c>TrackerDevice</c>. Judging that
    /// by hand meant a run to the beach, a suitcase, a look, and a restart
    /// per number, and it wore the person doing it out. This does the same
    /// thing to the nearest suitcase in the unattended run, through the
    /// same methods the suitcase itself calls, and photographs it from
    /// above and from the front.
    /// </summary>
    internal static class LuggageShot
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static IEnumerator Run(string folder)
        {
            Character character = Character.localCharacter;
            if (character == null || !Tracker.TrackerItem.Registered) yield break;

            Luggage luggage = Nearest(character.Center, out List<Transform> spots);
            if (luggage == null)
            {
                Plugin.Logger.LogWarning("Luggage shot: no suitcase with spawn spots near the character.");
                yield break;
            }

            // The lid, opened for the camera. The game opens it through an
            // RPC that also spawns loot; the animation alone is enough here.
            object anim = typeof(Luggage).GetField("anim", Hidden)?.GetValue(luggage);
            anim?.GetType().GetMethod("Play", new[] { typeof(string) })?.Invoke(anim, new object[] { "Luggage_Open" });

            Transform spot = spots[0];
            GameObject spawned;
            try
            {
                spawned = PhotonNetwork.Instantiate(
                    "0_Items/" + Tracker.TrackerItem.Prefab.name, spot.position, spot.rotation, 0);
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Luggage shot: could not spawn: {error.Message}");
                yield break;
            }

            Item item = spawned == null ? null : spawned.GetComponent<Item>();
            if (item == null) yield break;

            // Spawner.SpawnItems, step for step, on the real suitcase.
            if (luggage.spawnUpTowardsTarget != null)
                item.transform.up = (luggage.spawnUpTowardsTarget.position - item.transform.position).normalized;
            if (luggage.centerItemsVisually)
                item.transform.position += spot.position - item.Center();

            Invoke(luggage, "OffsetSpawn", item);
            Invoke(luggage, "InitializePhysics", item);

            // The kinematic RPC, the slide to the middle, and the lid.
            yield return new WaitForSecondsRealtime(1.5f);

            Plugin.Logger.LogInfo(
                $"Luggage shot: '{luggage.name}' at {luggage.transform.position:F2}, device at " +
                $"{item.transform.position:F2} local {luggage.transform.InverseTransformPoint(item.transform.position):F3} " +
                $"euler {item.transform.eulerAngles:F0} kinematic {item.rig.isKinematic}");

            Camera camera = Camera.main;
            if (camera == null) yield break;

            Vector3 centre = Vector3.zero;
            foreach (Transform s in spots) centre += s.position;
            centre /= spots.Count;

            List<Behaviour> suspended = HandShot.Suspend(camera);
            HandShot.CameraState saved = HandShot.CameraState.Save(camera);

            // From above, square on, the way the suitcase is looked into;
            // then from the front and low, the way it is met on the sand.
            Vector3 toPlayer = character.Center - centre;
            toPlayer.y = 0f;
            toPlayer.Normalize();

            camera.transform.position = centre + Vector3.up * 1.8f + toPlayer * 0.3f;
            camera.transform.rotation = Quaternion.LookRotation(centre - camera.transform.position, -toPlayer);
            yield return Shoot(Path.Combine(folder, "luggage-top.png"));

            camera.transform.position = centre + Vector3.up * 0.7f + toPlayer * 1.4f;
            camera.transform.rotation = Quaternion.LookRotation(centre - camera.transform.position, Vector3.up);
            yield return Shoot(Path.Combine(folder, "luggage-front.png"));

            saved.Restore(camera);
            foreach (Behaviour behaviour in suspended)
                if (behaviour != null) behaviour.enabled = true;

            Plugin.Logger.LogInfo($"Luggage shot: wrote 2 photographs to {folder}");
        }

        private static Luggage Nearest(Vector3 from, out List<Transform> spots)
        {
            Luggage best = null;
            spots = null;
            float bestDistance = float.MaxValue;

            foreach (Luggage luggage in Luggage.ALL_LUGGAGE)
            {
                if (luggage == null) continue;

                var found = new List<Transform>();
                if (luggage.spawnSpots != null) found.AddRange(luggage.spawnSpots);
                if (luggage.weightedSpawnSpots != null)
                    foreach (var entry in luggage.weightedSpawnSpots)
                        if (entry?.spawnSpots != null) found.AddRange(entry.spawnSpots);
                found.RemoveAll(t => t == null);
                if (found.Count == 0) continue;

                float distance = Vector3.Distance(from, luggage.transform.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = luggage;
                spots = found;
            }

            return best;
        }

        private static void Invoke(Luggage luggage, string method, Item item)
        {
            MethodInfo found = luggage.GetType().GetMethod(method, Hidden);
            if (found == null)
            {
                Plugin.Logger.LogWarning($"Luggage shot: the game has no '{method}' any more.");
                return;
            }
            found.Invoke(luggage, new object[] { item });
        }

        private static IEnumerator Shoot(string path)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            try { ScreenCapture.CaptureScreenshot(path); }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Luggage shot: could not photograph: {error.Message}");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
        }
    }
}
