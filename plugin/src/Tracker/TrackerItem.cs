using Photon.Pun;
using UnityEngine;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// The device as something a climber can find in a suitcase, pick up and
    /// carry in both hands.
    ///
    /// Everything here is built at runtime. The documented way to add an item
    /// to this game is to author a prefab in the Unity editor and bake an
    /// AssetBundle, and that route was refused on purpose: it pins the mod to
    /// one exact editor version, and a bundle carrying shaders fails quietly
    /// when that version moves. What PEAKLib actually asks for is smaller than
    /// the documentation implies — <c>new ItemContent(item)</c> takes a live
    /// <see cref="Item"/> component on a live GameObject, and nothing in it
    /// cares where that GameObject came from.
    ///
    /// The item then joins the game's own loot tables without a single patch:
    /// <c>LootData.PopulateLootData</c> walks the item database and reads a
    /// <see cref="LootData"/> component off whatever it finds, so declaring a
    /// rarity and a set of spawn pools is the whole of "put it in chests".
    /// </summary>
    internal static class TrackerItem
    {
        /// <summary>
        /// What the item is called. Also, through PEAKLib, half of what its
        /// network identity is hashed from — so changing it changes the item's
        /// id and orphans any that were already lying in somebody's world.
        /// </summary>
        internal const string ItemName = "Hiking GPS";

        internal static GameObject Prefab { get; private set; }

        internal static bool Registered => Prefab != null;

        /// <summary>
        /// Where the hands go.
        ///
        /// Two empties, named exactly this, looked up by the game with
        /// <c>item.transform.Find</c> at the moment somebody picks the item up.
        /// A missing one is not a warning and not a shrug: it is a null
        /// dereference inside <c>CharacterItems</c>, and the item is
        /// unpickupable from then on.
        ///
        /// They are made here rather than drawn in Blender because they carry
        /// no geometry — a position and a rotation each — and because the only
        /// way to judge them is to look at a character actually holding the
        /// thing. Round-tripping a model through another program for two
        /// numbers that are going to be adjusted by eye would be a slow way to
        /// do a fast job.
        ///
        /// The convention was measured off the game's own items rather than
        /// guessed, by walking the whole item database: X is 270 and Z is 0 on
        /// every simple one, and the only thing that varies is the splay about
        /// Y, which is 180 give or take fifteen or thirty.
        ///
        /// The positions are in the item's own frame — which is not the
        /// model's, and that sign cost a photograph.
        ///
        /// The model is turned 180 degrees inside the item so the screen faces
        /// its owner, so the item's +Z is the back of the case and the screen
        /// is on -Z. Grips at a negative Z therefore put both hands in front of
        /// the glass, and the first attempt did exactly that: a device held
        /// correctly, in both hands, with a thumb across the map.
        /// </summary>
        //
        // The numbers live in the config, not here. Three passes at them were
        // reasoned from the case's measurements and each was wrong in a way
        // only a person holding the thing could see: hands meeting behind the
        // case, palms on its sides but wrists wrenched. A setting can be
        // changed in a running game and looked at in the same minute; a
        // constant costs a build and a restart per guess. Once a grip is found
        // by eye, its numbers become the defaults in PluginConfig.
        private static float GripAcross => Plugin.Settings.TrackerGripAcross.Value;
        private static float GripBehind => Plugin.Settings.TrackerGripBehind.Value;
        private static float GripHeight => Plugin.Settings.TrackerGripHeight.Value;
        private static float GripAngleX => Plugin.Settings.TrackerGripAngleX.Value;
        private static float GripAngleY => Plugin.Settings.TrackerGripAngleY.Value;
        private static float GripAngleZ => Plugin.Settings.TrackerGripAngleZ.Value;

        /// <summary>
        /// How heavy it is, in the units the game's own items use.
        ///
        /// Light. It is a plastic box the size of a hand, and PEAK's mass shows
        /// up as how much a thrown item carries and how far a dropped one
        /// bounces, not as anything the carrier feels.
        /// </summary>
        private const float Mass = 1.2f;

        /// <summary>
        /// Builds the item and hands it to PEAKLib.
        ///
        /// Deliberately not called from the plugin's Awake. PEAKLib is a soft
        /// dependency, and touching a type from an assembly that is not there
        /// throws where it is used rather than where it is imported — so this
        /// is only ever reached once something has checked that PEAKLib loaded.
        /// </summary>
        internal static bool Register()
        {
            if (Registered) return true;

            GameObject model = TrackerObject.Build("Model");
            if (model == null) return false;

            // The whole device, turned to face its owner.
            //
            // The game orients a held item by pointing its +Z along
            // `character.data.lookDirection` — that is, away from the face. The
            // model's +Z is the screen, so left alone the player is handed a
            // navigator held backwards, reading the back cover while the map
            // shows the scenery. One rotation inside the item fixes it: the
            // item's forward becomes the back of the case, which is what should
            // point away from a person reading it.
            var device = new GameObject("Hiking_GPS");
            model.transform.SetParent(device.transform, worldPositionStays: false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Kept out of the world and out of every scene load. This object is
            // the pattern copies are made from, not a copy.
            Object.DontDestroyOnLoad(device);
            device.SetActive(false);

            // Scaled on the model, never on the root.
            //
            // The root carries the Rigidbody, and the game welds each hand to
            // it with a FixedJoint. Unity's joints do not support a scaled
            // body: the joint frames come out wrong by the scale, the solver
            // pulls against the error every step, and what a player sees is
            // the palm holding still while the forearm winds slowly through
            // ninety degrees. Three passes at the grip angles could not fix
            // it because none of them was the cause. The model and its
            // collider scale together as children; the grip points and the
            // centre of mass are multiplied by hand.
            float scale = Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);
            model.transform.localScale = Vector3.one * scale;
            PlaceModel(model.transform);

            AddGrips(device.transform);

            var view = device.AddComponent<PhotonView>();
            view.ObservedComponents = new System.Collections.Generic.List<Component>();
            view.Synchronization = ViewSynchronization.Off;

            Item item = device.AddComponent<Item>();
            item.mass = Mass;
            item.defaultPos = HoldPos;

            // The game's own scale handling is left on. It resets the root's
            // scale to one whenever the item changes hands, and to a half in a
            // backpack; with the size on the model rather than the root, that
            // is now harmless — and a device that shrinks to fit a backpack is
            // what every other item does.
            item.forceScale = true;

            // Every one of these is read by PEAKLib while registering, and a
            // null string there is an exception inside its translation setup
            // rather than a blank label in the game.
            item.UIData = new Item.ItemUIData
            {
                itemName = ItemName,
                icon = Icon(),
                hasMainInteract = true,
                mainInteractPrompt = "Pick up",
                hasSecondInteract = false,
                secondaryInteractPrompt = string.Empty,
                canDrop = true,
                canPocket = true,
                canBackpack = true,
                canThrow = true
            };

            AddLoot(device);

            Prefab = device;
            WatchGripSettings();
            return true;
        }

        /// <summary>
        /// What the item looks like in an inventory slot.
        ///
        /// The drawn case, which has been in the assembly since before there
        /// was a model: the same device by the same hand, and at the size a
        /// slot actually shows — about forty pixels — a drawing reads better
        /// than a photograph of the mesh would. Without one the slot draws a
        /// white square, which is not a crash and looks like one.
        /// </summary>
        private static Texture2D Icon()
        {
            Sprite drawn = Minimap.Navigator.Body;
            return drawn == null ? null : drawn.texture;
        }

        /// <summary>
        /// Two empties for the hands, mirrored about the case.
        ///
        /// The rotations are the measured convention rather than something that
        /// looked plausible: -90 about X turns a hand from lying flat to
        /// gripping, and the 15 degrees either way is what turns two parallel
        /// hands into two hands holding the same object.
        /// </summary>
        private static void AddGrips(Transform root)
        {
            foreach (string name in new[] { "Hand_L", "Hand_R" })
            {
                var holder = new GameObject(name);
                holder.transform.SetParent(root, worldPositionStays: false);
            }
            PlaceGrips(root);
        }

        /// <summary>
        /// Puts both grip points where the settings currently say. Called when
        /// the device is built and again whenever a grip setting changes.
        /// </summary>
        /// <summary>
        /// Where the case sits inside the item root, which is the point the
        /// game holds an item at. Multiplied by Scale like the grips, so the
        /// device keeps its proportions as it grows.
        /// </summary>
        internal static Vector3 ModelOffset
        {
            get
            {
                float scale = Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);
                return new Vector3(0f, Plugin.Settings.TrackerOffsetUp.Value, Plugin.Settings.TrackerOffsetAway.Value) * scale;
            }
        }

        /// <summary>Where the game holds it: right, up and forward of the head.</summary>
        private static Vector3 HoldPos => new Vector3(
            Plugin.Settings.TrackerHoldX.Value, Plugin.Settings.TrackerHoldY.Value, Plugin.Settings.TrackerHoldZ.Value);

        private static void PlaceModel(Transform model)
        {
            if (model == null) return;
            model.localPosition = ModelOffset;
            model.localScale = Vector3.one * Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);

            // Turned to face its owner, then tipped back about the item's own
            // sideways axis so the top of the case leans towards the face.
            model.localRotation =
                Quaternion.AngleAxis(Plugin.Settings.TrackerTilt.Value, Vector3.right) * Quaternion.Euler(0f, 180f, 0f);
        }

        private static void PlaceGrips(Transform root)
        {
            Place(root.Find("Hand_L"), -1f);
            Place(root.Find("Hand_R"), 1f);
        }

        /// <summary>
        /// One hand, at <paramref name="side"/> -1 for the left and +1 for the
        /// right. The settings describe the left hand; the right is its
        /// reflection in the plane down the middle of the case, which for
        /// Unity's Euler angles is the same X with Y and Z negated. Checked
        /// against the game's own items: every pair in the database, bottles
        /// and compass alike, obeys exactly that.
        /// </summary>
        private static void Place(Transform hand, float side)
        {
            if (hand == null) return;
            float scale = Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);
            hand.localPosition = new Vector3(side * GripAcross, GripHeight, GripBehind) * scale;
            hand.localEulerAngles = side < 0f
                ? new Vector3(GripAngleX, GripAngleY, GripAngleZ)
                : new Vector3(GripAngleX, -GripAngleY, -GripAngleZ);
        }

        /// <summary>
        /// Moves the grip points on the pattern and on every device already in
        /// the world, so a setting changed mid-game is seen in the same game.
        ///
        /// The game welds a hand to these points once, at pick-up, with a
        /// <c>FixedJoint</c>; a device already in somebody's hands keeps its old
        /// grip until it is dropped and taken again. That is said in the log
        /// rather than worked around, because re-welding a live joint is the
        /// game's business and a drop costs a second.
        /// </summary>
        private static void RefreshGrips()
        {
            if (!Registered) return;

            PlaceGrips(Prefab.transform);
            PlaceModel(Prefab.transform.Find("Model"));
            Prefab.GetComponent<Item>().defaultPos = HoldPos;

            ushort id = Prefab.GetComponent<Item>().itemID;
            int moved = 0;
            foreach (Item item in Object.FindObjectsOfType<Item>(true))
            {
                if (item.itemID != id || item.gameObject == Prefab) continue;
                PlaceGrips(item.transform);
                PlaceModel(item.transform.Find("Model"));
                item.defaultPos = HoldPos;
                moved++;
            }

            Plugin.Logger.LogInfo(
                $"Grip: across {GripAcross:0.000} behind {GripBehind:0.000} height {GripHeight:0.000} " +
                $"angles ({GripAngleX:0}, {GripAngleY:0}, {GripAngleZ:0}) offset {ModelOffset:F3} hold {HoldPos:F2}; moved on {moved} device(s). " +
                "Drop and pick up to see it.");
        }

        private static void WatchGripSettings()
        {
            var s = Plugin.Settings;
            s.TrackerScale.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerTilt.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerHoldX.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerHoldY.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerHoldZ.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerOffsetUp.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerOffsetAway.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripAcross.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripBehind.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripHeight.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripAngleX.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripAngleY.SettingChanged += (_, __) => RefreshGrips();
            s.TrackerGripAngleZ.SettingChanged += (_, __) => RefreshGrips();
        }

        /// <summary>
        /// Which chests it turns up in, and how often.
        ///
        /// The beach on purpose. A map is worth most before the climb rather
        /// than after it, and a navigator found in the Citadel is a souvenir.
        /// Both figures are settings, because how rare a thing should be is a
        /// matter of taste and nobody's taste but the player's is binding here.
        /// </summary>
        private static void AddLoot(GameObject device)
        {
            var loot = device.AddComponent<LootData>();

            loot.Rarity = Plugin.Settings.TrackerRarity.Value;
            loot.spawnLocations = Plugin.Settings.TrackerSpawnPools.Value;
            loot.banInSolo = false;
        }
    }
}
