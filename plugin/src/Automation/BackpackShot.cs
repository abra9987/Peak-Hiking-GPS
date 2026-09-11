using System.Collections;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// The device in every slot of a backpack, photographed.
    ///
    /// A backpack shows what it carries on its outside, one item per slot,
    /// each drawn at half size and set at the slot's transform less half the
    /// item's centre of mass — which this device moves on purpose, to fall
    /// on its back, so how it sits on a backpack is not obvious from the
    /// code. A backpack is spawned on the sand rather than worn, because a
    /// character's own back hides its items from its own camera; the slots
    /// are the same either way.
    /// </summary>
    internal static class BackpackShot
    {
        internal static IEnumerator Run(string folder)
        {
            Character character = Character.localCharacter;
            if (character == null || !Tracker.TrackerItem.Registered) yield break;

            // Out on the sand, far enough that the camera behind it is clear
            // of the character; and whatever the hand shot left in the hands
            // is hidden meanwhile, since it sits exactly where that camera
            // would otherwise stand.
            var hidden = new List<Renderer>();
            Item held = character.data == null ? null : character.data.currentItem;
            if (held != null)
                foreach (Renderer r in held.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }

            Vector3 where = character.Center + character.transform.forward * 4.0f + Vector3.up * 0.3f;
            GameObject pack;
            try
            {
                pack = PhotonNetwork.Instantiate("0_Items/Backpack", where, Quaternion.identity, 0);
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Backpack shot: could not spawn a backpack: {error.Message}");
                yield break;
            }

            Backpack backpack = pack == null ? null : pack.GetComponent<Backpack>();
            if (backpack == null)
            {
                Plugin.Logger.LogWarning("Backpack shot: the spawned object is not a Backpack.");
                yield break;
            }

            // Let it land and settle before anything hangs off it.
            yield return new WaitForSecondsRealtime(2.5f);

            // The slots live on the backpack's visuals, not on the item.
            BackpackReference reference = BackpackReference.GetFromBackpackItem(backpack);
            Transform[] slotTransforms = reference.GetVisuals()?.backpackSlots;
            // Only the slots a backpack actually has. The visuals carry more
            // transforms than that, and the extra ones are never filled in
            // play.
            int slots = slotTransforms == null ? 0 : Mathf.Min(slotTransforms.Length, backpack.slotCount);
            var devices = new List<Item>();

            for (byte slot = 0; slot < slots; slot++)
            {
                GameObject spawned;
                try
                {
                    spawned = PhotonNetwork.Instantiate(
                        "0_Items/" + Tracker.TrackerItem.Prefab.name, new Vector3(0f, -500f, 0f), Quaternion.identity, 0);
                }
                catch (System.Exception error)
                {
                    Plugin.Logger.LogWarning($"Backpack shot: could not spawn a device: {error.Message}");
                    yield break;
                }

                Item item = spawned == null ? null : spawned.GetComponent<Item>();
                if (item == null) continue;

                item.PutInBackpackRPC(slot, reference);
                devices.Add(item);
            }

            yield return new WaitForSecondsRealtime(1f);

            for (int i = 0; i < devices.Count; i++)
            {
                Transform slot = slotTransforms[i];
                Plugin.Logger.LogInfo(
                    $"Backpack shot: slot {i} at {slot.position:F2} euler {slot.eulerAngles:F0}; device at " +
                    $"{devices[i].transform.position:F2} euler {devices[i].transform.eulerAngles:F0} " +
                    $"scale {devices[i].transform.localScale:F2}");
            }

            Camera camera = Camera.main;
            if (camera == null) yield break;

            List<Behaviour> suspended = HandShot.Suspend(camera);
            HandShot.CameraState saved = HandShot.CameraState.Save(camera);

            Vector3 centre = backpack.Center();
            Vector3 back = -backpack.transform.forward;

            // Straight at the back, then from either side, then from above.
            var views = new (string Name, Vector3 Offset)[]
            {
                ("back",  back * 1.1f + Vector3.up * 0.25f),
                ("left",  (back + backpack.transform.right).normalized * 1.1f + Vector3.up * 0.35f),
                ("right", (back - backpack.transform.right).normalized * 1.1f + Vector3.up * 0.35f),
                ("above", back * 0.6f + Vector3.up * 1.0f),
                ("front", -back * 1.1f + Vector3.up * 0.3f),
            };

            foreach (var view in views)
            {
                camera.transform.position = centre + view.Offset;
                camera.transform.rotation = Quaternion.LookRotation(centre - camera.transform.position, Vector3.up);
                yield return Shoot(Path.Combine(folder, $"backpack-{view.Name}.png"));
            }

            saved.Restore(camera);
            foreach (Behaviour behaviour in suspended)
                if (behaviour != null) behaviour.enabled = true;
            foreach (Renderer r in hidden)
                if (r != null) r.enabled = true;

            Plugin.Logger.LogInfo($"Backpack shot: {devices.Count} device(s) in {slots} slot(s); wrote {views.Length} photographs to {folder}");
        }

        private static IEnumerator Shoot(string path)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            try { ScreenCapture.CaptureScreenshot(path); }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Backpack shot: could not photograph: {error.Message}");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
        }
    }
}
