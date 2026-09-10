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
        // On the case, not beside it. The first pass put them a centimetre
        // outside each edge and two behind the back, which is invisible at the
        // device's true size and absurd once Scale is raised: the grips scale
        // with the case but hands do not, so at three times the palms hovered
        // three centimetres clear of it, holding nothing.
        private const float GripAcross = 0.042f;   // inside the 89.6 mm case, palms on its sides
        private const float GripBehind = 0.006f;   // just behind mid-depth, fingers round the back
        private const float GripHeight = 0.040f;   // low, near the buttons, the way a handheld is held
        private const float GripSplay = 15f;

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

            // Scaled at the root so that the case, its collision box and the
            // grip points all grow together. Scaling only the model would leave
            // the hands where they were and put them inside a bigger case.
            float scale = Mathf.Clamp(Plugin.Settings.TrackerScale.Value, 0.5f, 6f);
            device.transform.localScale = Vector3.one * scale;

            AddGrips(device.transform);

            var view = device.AddComponent<PhotonView>();
            view.ObservedComponents = new System.Collections.Generic.List<Component>();
            view.Synchronization = ViewSynchronization.Off;

            Item item = device.AddComponent<Item>();
            item.mass = Mass;

            // Keep the size we were given.
            //
            // Item.SetState forces localScale back to one every time an item
            // changes hands — held, on the ground, and to a half in a backpack.
            // With it left on, Scale did nothing the moment anybody picked the
            // device up: it sat oversized in the chest and shrank back to its
            // true 90 millimetres in the hands, which read as two different
            // objects rather than one setting not working.
            item.forceScale = false;

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
            Grip(root, "Hand_L", -GripAcross, 180f + GripSplay);
            Grip(root, "Hand_R", GripAcross, 180f - GripSplay);
        }

        private static void Grip(Transform root, string name, float across, float turn)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(root, worldPositionStays: false);
            holder.transform.localPosition = new Vector3(across, GripHeight, GripBehind);
            holder.transform.localEulerAngles = new Vector3(270f, 0f, turn);
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
