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

        private void Awake()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                _buttons[i] = Find(transform, Names[i]);
                if (_buttons[i] != null) _rest[i] = _buttons[i].localPosition;
            }

            All.Add(this);
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

        private void Update()
        {
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
        /// Presses one button on every device there is.
        ///
        /// Every device shows the same map from the same camera, so they press
        /// together too. There is normally one; the preview run stands two side
        /// by side, and a shared run could have one in each of four hands.
        /// </summary>
        internal static void PressAll(int index)
        {
            if (index < 0 || index >= Names.Length) return;

            foreach (TrackerDevice device in All)
            {
                if (device == null) continue;

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
