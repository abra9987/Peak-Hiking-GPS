using System.Collections.Generic;
using UnityEngine;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// The moving parts of a device standing in the world: its buttons going
    /// down when they are pressed, and coming back up.
    ///
    /// The drawn version faked a press by swapping in a smaller picture, which
    /// is what a drawing can do. A mesh does not need faking — the three button
    /// caps are separate objects with their origins at the base of each, so a
    /// press is a transform moved six tenths of a millimetre and released.
    /// </summary>
    internal sealed class TrackerDevice : MonoBehaviour
    {
        /// <summary>
        /// How far a button travels, measured from the model rather than
        /// chosen: at full travel the cap's face still stands half a millimetre
        /// proud of the floor of its well, so nothing sinks out of sight and no
        /// gap opens at the skirt.
        /// </summary>
        private const float Travel = 0.0006f;

        /// <summary>How long a press lasts, matching the drawn version's.</summary>
        private const float Hold = 0.09f;

        /// <summary>How quickly a button settles back up.</summary>
        private const float Rise = 14f;

        private static readonly string[] Names =
        {
            "Tracker_Button_L",   // zoom out, the minus
            "Tracker_Button_M",   // tilt, the pin
            "Tracker_Button_R"    // zoom in, the plus
        };

        private static readonly List<TrackerDevice> All = new List<TrackerDevice>();

        private readonly Transform[] _buttons = new Transform[3];
        private readonly Vector3[] _rest = new Vector3[3];
        private readonly float[] _pressedUntil = new float[3];
        private readonly float[] _depth = new float[3];

        private Material _screen;
        private Item _item;
        private bool _live;
        private Texture _shown;

        private void Awake()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                _buttons[i] = Find(transform, Names[i]);
                if (_buttons[i] != null) _rest[i] = _buttons[i].localPosition;
            }

            // A screen of its own. The renderer arrives sharing one material
            // with every other device, and the map has to go on this one's
            // screen or not, so it gets a copy.
            Transform screen = Find(transform, TrackerObject.ScreenPart);
            var renderer = screen == null ? null : screen.GetComponent<Renderer>();
            if (renderer != null)
            {
                _screen = renderer.material;
                TrackerObject.SetTexture(_screen, null);
                TrackerObject.SetColour(_screen, TrackerObject.ScreenOff);
            }

            All.Add(this);
        }

        /// <summary>
        /// Whether this is the device in the local character's hands.
        ///
        /// Only that one is switched on. Every device draws from the same map,
        /// and the first version put it on all of them at once, so three
        /// navigators dropped on the sand all lit up and all zoomed together —
        /// three screens showing one instrument. A device on the ground is
        /// off; a device in somebody else's hands is theirs, not this
        /// player's. With nobody at the keyboard at all — the preview run —
        /// every screen stays on, since there is nobody to hold one.
        /// </summary>
        private bool Held()
        {
            Character player = Character.localCharacter;
            if (player == null || player.data == null) return true;

            if (_item == null) _item = GetComponentInParent<Item>();
            return _item != null && player.data.currentItem == _item;
        }

        private void UpdateScreen()
        {
            if (_screen == null) return;

            bool live = Held();
            Texture map = live ? TrackerObject.Map : null;

            if (live == _live && map == _shown) return;
            _live = live;
            _shown = map;

            TrackerObject.SetTexture(_screen, map);
            TrackerObject.SetColour(_screen, map != null ? Color.white : TrackerObject.ScreenOff);
        }

        private void OnDestroy() => All.Remove(this);

        /// <summary>
        /// Makes the device land screen-up when it is dropped or spilled out of
        /// a suitcase.
        ///
        /// It is spawned with no rotation at all, which stands it on its bottom
        /// edge, and from there it topples onto whichever of its two large flat
        /// faces chance picks. Half the time that is the glass, and a navigator
        /// lying face-down in a chest looks like a mistake even though nothing
        /// went wrong.
        ///
        /// The fix is the one a real device uses: everything heavy — battery,
        /// board, antenna — sits behind the screen, so it falls onto its back.
        /// Moving the centre of mass towards the back cover says the same thing
        /// to the physics engine, and leaves the item free to be thrown and
        /// knocked about as before.
        ///
        /// Backwards and upwards, never downwards. The first version dropped
        /// the weight towards the bottom edge, and a slab with its weight low
        /// is a roly-poly: however it was thrown it righted itself and stood
        /// upright on that edge. High and back is the opposite: standing, the
        /// weight hangs behind the edge it stands on with a long lever, and
        /// over it goes. Lying flat the height makes no difference. The shape
        /// of the collider, in <see cref="TrackerObject"/>, leaves it no edge
        /// to balance on; this decides which way it falls and how eagerly.
        ///
        /// In Start rather than Awake: Item.Awake adds the Rigidbody and then
        /// reads its centre of mass into a field of its own, so setting this any
        /// earlier is either overwritten or leaves the two disagreeing.
        /// </summary>
        private void Start()
        {
            var rig = GetComponentInParent<Rigidbody>();
            if (rig == null) return;

            // In the item's frame, +Z is the back of the case: the model is
            // turned 180 degrees inside the item so the glass faces its owner.
            // In the root's units, which are unscaled: the size lives on the
            // model below, so the bias is grown by hand to match it.
            float scale = Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);
            var bias = new Vector3(0f, 0.030f, 0.015f) * scale + TrackerItem.ModelOffset;

            rig.centerOfMass = bias;

            // Never let it sleep. PhysX puts a body to sleep once its motion
            // drops under a threshold, and a thin slab landing nearly upright
            // moves under that threshold before gravity has had a chance to
            // lean on it — so it froze on its bottom edge, tilted a few
            // degrees, and stayed there. One rigidbody that never sleeps
            // costs nothing worth counting.
            rig.sleepThreshold = 0f;

            var item = GetComponentInParent<Item>();
            if (item != null) item.centerOfMass = bias;
        }

        private bool _centred;

        /// <summary>
        /// Slides a device that has just been laid in a suitcase to the middle
        /// of the case, along its length.
        ///
        /// A suitcase places its items on spawn spots — 45 centimetres from
        /// the middle in the small one — and centres each item's bounds on
        /// its spot, which is built for things a hand's length across. This
        /// device is half a metre long at the size it is played at, so on a
        /// spot it lay with its antenna through the end wall, whichever way
        /// it was turned. It is moved to the middle of the case, along and
        /// across; its height above the floor and its rotation are the
        /// game's and stay as given.
        ///
        /// Done once, the first frame the item is found kinematic on the
        /// ground, which is how a suitcase holds what it has laid out until
        /// somebody takes it. Nothing else leaves an item in that state.
        /// </summary>
        private void CentreInLuggage()
        {
            if (_centred) return;
            if (_item == null) _item = GetComponentInParent<Item>();
            if (_item == null || _item.rig == null) return;
            if (_item.itemState != ItemState.Ground || !_item.rig.isKinematic) return;

            _centred = true;

            // The suitcase this device was laid in is the one with a spawn
            // spot under it. Not the nearest by its root: a suitcase's root
            // sits at one edge, so the nearest root can belong to the closed
            // case next door, and the device was carried off into that one —
            // which showed as a second item in a case that had held one.
            Luggage nearest = null;
            Vector3 centre = Vector3.zero;
            float best = 0.25f;
            foreach (Luggage luggage in Luggage.ALL_LUGGAGE)
            {
                if (luggage == null) continue;

                var lists = new List<List<Transform>>();
                if (luggage.spawnSpots != null) lists.Add(luggage.spawnSpots);
                if (luggage.weightedSpawnSpots != null)
                    foreach (var entry in luggage.weightedSpawnSpots)
                        if (entry != null && entry.spawnSpots != null) lists.Add(entry.spawnSpots);

                Vector3 sum = Vector3.zero;
                int count = 0;
                float closest = float.MaxValue;
                foreach (var list in lists)
                    foreach (Transform spot in list)
                    {
                        if (spot == null) continue;
                        sum += luggage.transform.InverseTransformPoint(spot.position);
                        count++;
                        Vector3 flat = spot.position - _item.transform.position;
                        flat.y = 0f;
                        closest = Mathf.Min(closest, flat.magnitude);
                    }

                if (count == 0 || closest >= best) continue;
                best = closest;
                nearest = luggage;
                centre = sum / count;
            }
            if (nearest == null) return;

            Vector3 local = nearest.transform.InverseTransformPoint(_item.transform.position);
            local.x = centre.x;
            local.z = centre.z;
            Vector3 world = nearest.transform.TransformPoint(local);

            _item.transform.position = world;
            _item.rig.position = world;
        }

        private void Update()
        {
            UpdateScreen();
            CentreInLuggage();

            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] == null) continue;

                float wanted = Time.unscaledTime < _pressedUntil[i] ? 1f : 0f;

                // Down at once, up gently. A button that eases down as slowly
                // as it comes up feels like a sponge; the press should land on
                // the same frame as the click it makes.
                _depth[i] = wanted > _depth[i]
                    ? wanted
                    : Mathf.MoveTowards(_depth[i], wanted, Rise * Time.unscaledDeltaTime);

                // Into the case: the model's own -Z, which is the same axis the
                // exporter's +0.6 mm became when the blob was built.
                _buttons[i].localPosition = _rest[i] + new Vector3(0f, 0f, -Travel * _depth[i]);
            }
        }

        /// <summary>
        /// Presses one button on the device that is switched on — the one in
        /// the local player's hands, or every one in the preview run.
        /// </summary>
        internal static void PressAll(int index)
        {
            if (index < 0 || index >= Names.Length) return;

            foreach (TrackerDevice device in All)
            {
                if (device == null || !device._live) continue;

                device._pressedUntil[index] = Time.unscaledTime + Hold;
            }
        }

        /// <summary>
        /// A descendant by name, at any depth.
        ///
        /// <c>Transform.Find</c> only looks at direct children, and the parts
        /// sit one level down inside the item — under a child that carries the
        /// rotation which turns the device to face whoever is holding it.
        /// </summary>
        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == name) return child;
            }

            return null;
        }
    }
}
