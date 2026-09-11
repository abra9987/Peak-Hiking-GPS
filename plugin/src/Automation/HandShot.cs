using System.Collections;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PeakMapInteractive.Automation
{
    /// <summary>
    /// Puts the device in the character's hands and photographs it from
    /// several sides, unattended.
    ///
    /// This exists because the grip points cannot be judged any other way. They
    /// are two empty transforms, and what they decide is where two hands land
    /// on a case ninety millimetres across and at what angle the wrists turn —
    /// which is a question about a photograph, not about numbers. Guessing them
    /// and then climbing a mountain to look would cost five minutes an attempt;
    /// this costs a minute and nobody's attention.
    ///
    /// The camera is borrowed the way the map capture borrows it: the game's
    /// own, with everything that drives it switched off and put back
    /// afterwards. A second camera of our own would be simpler and does not
    /// survive contact with this renderer.
    /// </summary>
    internal static class HandShot
    {
        /// <summary>Where the camera is put, relative to the character, per shot.</summary>
        private static readonly (string Name, float Yaw, float Pitch, float Distance)[] Views =
        {
            ("whole", 0f, 12f, 2.6f),
            ("front", 0f, 0f, 0.42f),
            ("side", 55f, 5f, 0.42f),
            ("above", 15f, -40f, 0.40f)
        };

        internal static IEnumerator Run(string folder)
        {
            Character character = Character.localCharacter;

            if (character == null)
            {
                Plugin.Logger.LogWarning("Hand shot: there is no local character to hand it to.");
                yield break;
            }

            if (!Tracker.TrackerItem.Registered)
            {
                Plugin.Logger.LogWarning("Hand shot: the device is not a registered item.");
                yield break;
            }

            GameObject spawned = Spawn(character);
            if (spawned == null) yield break;

            Item item = spawned.GetComponent<Item>();

            // Equipping is not instant: the game waits for the hip to exist,
            // moves the item onto the hold position over several fixed
            // updates, and only then welds the hands to it. Photographing
            // before that has finished is a photograph of an item in mid-air.
            // Picked up the way a player picks it up, so it lands in a slot
            // and the slot's icon is in the picture; equipped outright as a
            // fallback if the pickup did not take.
            item.Interact(character);

            // The pickup destroys the object picked up and makes another to
            // equip, a few frames later than the first version waited for.
            for (int i = 0; i < 200 && character.data.currentItem == null; i++)
                yield return new WaitForFixedUpdate();
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();

            yield return Walk(character);

            // The object picked up is not the object held: a pickup puts the
            // item into a slot and the game makes a fresh one to equip, so
            // the one spawned here may be gone by now.
            Item held = character.data.currentItem;
            if (held == null)
            {
                Plugin.Logger.LogWarning("Hand shot: nothing ended up in the hands.");
                yield break;
            }
            Describe(character, held.gameObject);

            Camera camera = Camera.main;
            if (camera == null)
            {
                Plugin.Logger.LogWarning("Hand shot: no camera.");
                yield break;
            }

            // First, the shot that needs no camera work at all: the game's own
            // view, untouched. It is the only one that is literally what a
            // player sees, and every attempt to improve on it by moving the
            // camera has so far produced a photograph of the back of a head.
            yield return Shoot(Path.Combine(folder, "hand-player.png"));

            List<Behaviour> suspended = Suspend(camera);
            CameraState saved = CameraState.Save(camera);

            // The same view, tilted down — a player looking at the thing they
            // are carrying. This is the shot that decides whether a map on a
            // held device can be read at all, or whether it is a decoration
            // somewhere below the bottom of the screen.
            foreach (float pitch in new[] { 25f, 45f })
            {
                camera.transform.rotation = saved.Rotation * Quaternion.Euler(pitch, 0f, 0f);
                yield return Shoot(Path.Combine(folder, $"hand-looking-{pitch:00}.png"));
            }

            foreach (var view in Views)
            {
                Aim(camera, character, view.Yaw, view.Pitch, view.Distance);

                yield return Shoot(Path.Combine(folder, $"hand-{view.Name}.png"));
            }

            saved.Restore(camera);
            foreach (Behaviour behaviour in suspended)
                if (behaviour != null) behaviour.enabled = true;

            Plugin.Logger.LogInfo($"Hand shot: wrote {Views.Length + 3} photographs to {folder}");
        }

        private static IEnumerator Shoot(string path)
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            try { ScreenCapture.CaptureScreenshot(path); }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Hand shot: could not photograph: {error.Message}");
                yield break;
            }

            // The capture lands on a later frame; moving the camera before it
            // does photographs the move.
            yield return new WaitForSecondsRealtime(1.5f);
        }

        /// <summary>
        /// Walks a couple of paces before anything is photographed.
        ///
        /// A character standing still holds an item differently from one on the
        /// move: the idle pose brings the arms in towards the body, and the
        /// hand IK is blended against whatever the animator is doing. Judging a
        /// grip from a photograph of somebody standing perfectly still is
        /// judging the pose the player will see least of.
        ///
        /// The input is written rather than pressed. Nobody is at the keyboard,
        /// so the game's own sampling fills `movementInput` with zero every
        /// frame; writing it afterwards, from a coroutine, lands after that and
        /// before the movement reads it. If a future version of the game
        /// samples later this simply stops working, which the logged speed will
        /// say plainly.
        /// </summary>
        private static IEnumerator Walk(Character character)
        {
            CharacterInput input = character.input;
            if (input == null) yield break;

            float until = Time.time + 2.5f;
            float fastest = 0f;

            while (Time.time < until)
            {
                input.movementInput = new Vector2(0f, 1f);

                // GetBodypartRig is internal to the game's assembly; the hip
                // is already on refs, and it is the same body.
                var hip = character.refs == null ? null : character.refs.hip;
                if (hip != null && hip.Rig != null)
                    fastest = Mathf.Max(fastest, hip.Rig.linearVelocity.magnitude);

                yield return null;
            }

            input.movementInput = Vector2.zero;

            Plugin.Logger.LogInfo($"Hand shot: walked at up to {fastest:0.00} m/s.");

            // A moment for the arms to settle out of the walk and back into
            // carrying, which is the pose worth looking at.
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>
        /// What actually ended up where.
        ///
        /// The first attempt produced a character plainly holding something and
        /// nothing visible between its hands, which could have been any of half
        /// a dozen things: an inactive clone, renderers that did not survive
        /// instantiation, or an item parked somewhere else entirely. Guessing
        /// between them costs a run each; measuring costs one.
        /// </summary>
        private static void Describe(Character character, GameObject spawned)
        {
            Item held = character.data.currentItem;

            Plugin.Logger.LogInfo(
                $"Hand shot: holding {(held == null ? "nothing" : held.name)}, " +
                $"clone active={spawned.activeInHierarchy}, at {spawned.transform.position.ToString("F2")}, " +
                $"scale {spawned.transform.localScale.ToString("F2")}");

            Renderer[] renderers = spawned.GetComponentsInChildren<Renderer>(includeInactive: true);
            Plugin.Logger.LogInfo($"Hand shot: {renderers.Length} renderer(s) on the clone.");

            foreach (Renderer renderer in renderers)
            {
                Plugin.Logger.LogInfo(
                    $"Hand shot:   {renderer.name} enabled={renderer.enabled} " +
                    $"visible={renderer.isVisible} material={(renderer.sharedMaterial == null ? "none" : renderer.sharedMaterial.name)} " +
                    $"bounds={renderer.bounds.size.ToString("F3")}");
            }

            Plugin.Logger.LogInfo(
                $"Hand shot: the device faces {spawned.transform.forward.ToString("F2")}, " +
                $"its up is {spawned.transform.up.ToString("F2")}.");

            Transform anchor = character.refs.animationItemTransform;
            Plugin.Logger.LogInfo(
                $"Hand shot: the rig's item anchor is at {(anchor == null ? "nowhere" : anchor.position.ToString("F2"))}, " +
                $"the character at {character.Center.ToString("F2")}.");
        }

        /// <summary>
        /// One of the device, in the world, beside the character.
        ///
        /// Through Photon rather than plain instantiation: Item wakes up
        /// expecting a view with an id, and an item made any other way spends
        /// the rest of its life logging about not having one.
        /// </summary>
        internal static GameObject Spawn(Character character)
        {
            string prefab = "0_Items/" + Tracker.TrackerItem.Prefab.name;
            Vector3 where = character.Center + Vector3.up * 0.5f;

            try
            {
                GameObject spawned = PhotonNetwork.Instantiate(prefab, where, Quaternion.identity, 0);

                if (spawned == null)
                {
                    Plugin.Logger.LogWarning($"Hand shot: '{prefab}' produced nothing.");
                    return null;
                }

                Plugin.Logger.LogInfo($"Hand shot: spawned '{prefab}'.");
                return spawned;
            }
            catch (System.Exception error)
            {
                Plugin.Logger.LogWarning($"Hand shot: could not spawn '{prefab}': {error.Message}");
                return null;
            }
        }

        /// <summary>
        /// Looks at the held device from a given angle, in the device's own frame.
        ///
        /// Two earlier versions aimed by the world's axes and then by the
        /// character's, and both produced beautifully composed photographs of
        /// something else: the back of a straw hat, and a foot. The trouble
        /// with both is that they describe where the camera stands rather than
        /// what it is looking at, and the thing being looked at is nine
        /// centimetres across and somewhere in a pair of hands.
        ///
        /// The device knows where its own face is: the screen is on its +Z. So
        /// angle zero is straight at the glass, and everything else is measured
        /// from there. It cannot miss.
        /// </summary>
        private static void Aim(Camera camera, Character character, float yaw, float pitch, float distance)
        {
            Item held = character.data.currentItem;

            if (held == null)
            {
                camera.transform.position = character.Center - character.transform.forward * distance;
                camera.transform.rotation =
                    Quaternion.LookRotation(character.Center - camera.transform.position, Vector3.up);
                return;
            }

            Transform device = held.transform;
            Vector3 target = device.position;

            // Back, not forward. The item's own +Z is the back of the case —
            // the game points that along the look direction — so the face worth
            // photographing is on the other side.
            Vector3 direction = device.rotation * (Quaternion.Euler(pitch, yaw, 0f) * Vector3.back);

            camera.transform.position = target + direction * distance;
            camera.transform.rotation =
                Quaternion.LookRotation(target - camera.transform.position, device.up);
        }

        /// <summary>
        /// Everything that would move the camera back, switched off and
        /// reported so it can be switched on again.
        ///
        /// The same list the map capture suspends, minus the screen canvases:
        /// the game's own interface is welcome in these pictures, since part of
        /// what is being judged is whether a device in the hands sits sensibly
        /// alongside it.
        /// </summary>
        internal static List<Behaviour> Suspend(Camera camera)
        {
            var suspended = new List<Behaviour>();

            foreach (Behaviour behaviour in camera.GetComponents<Behaviour>())
            {
                if (behaviour == null || behaviour is Camera || !behaviour.enabled) continue;

                behaviour.enabled = false;
                suspended.Add(behaviour);
            }

            foreach (var mover in Object.FindObjectsByType<MainCameraMovement>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mover == null || !mover.enabled) continue;

                mover.enabled = false;
                suspended.Add(mover);
            }

            return suspended;
        }

        /// <summary>Where the camera was before it was borrowed.</summary>
        internal readonly struct CameraState
        {
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;

            /// <summary>Where the game had the camera pointed before it was borrowed.</summary>
            public Quaternion Rotation => _rotation;

            private CameraState(Camera camera)
            {
                _position = camera.transform.position;
                _rotation = camera.transform.rotation;
            }

            public static CameraState Save(Camera camera) => new CameraState(camera);

            public void Restore(Camera camera)
            {
                if (camera == null) return;

                camera.transform.SetPositionAndRotation(_position, _rotation);
            }
        }
    }
}
