using PEAKLib.Core;
using PEAKLib.Items;
using UnityEngine;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// The one place PEAKLib is spoken to.
    ///
    /// Kept alone in its own class on purpose. PEAKLib is a soft dependency —
    /// the map, the navigator and the markers all work without it, and only the
    /// physical item needs it — and a missing assembly is not discovered when a
    /// file is compiled but when a method that mentions it is first run. So
    /// every mention lives in one method, which is only ever reached after
    /// something else has checked that PEAKLib is loaded.
    ///
    /// A mod that refuses to start because a second mod is absent is a worse
    /// mod for everyone who only wanted a map.
    /// </summary>
    internal static class TrackerRegistration
    {
        /// <summary>
        /// Builds the device and hands it to PEAKLib, which sees to the rest:
        /// a network prefab so copies can exist in a shared run, an id hashed
        /// from this mod and the item's name, and a place in the game's own
        /// item database. From there the game's loot tables find it themselves,
        /// by reading the <c>LootData</c> the item carries.
        ///
        /// Never call without checking <see cref="Plugin.HasPeakLib"/> first.
        /// </summary>
        internal static bool Register()
        {
            if (!TrackerItem.Register()) return false;

            Item item = TrackerItem.Prefab.GetComponent<Item>();
            if (item == null)
            {
                Plugin.Logger.LogError("Tracker: the built device has no Item on it.");
                return false;
            }

            ModDefinition mod = ModDefinition.GetOrCreate(Plugin.Instance.Info);
            new ItemContent(item).Register(mod);

            Plugin.Logger.LogInfo(
                $"Tracker: registered '{TrackerItem.ItemName}' as an item " +
                $"({Plugin.Settings.TrackerRarity.Value}, {Plugin.Settings.TrackerSpawnPools.Value}).");

            _modId = mod.Id;
            return true;
        }

        private static string _modId;

        /// <summary>
        /// Puts the item in the game's database if PEAKLib has not.
        ///
        /// PEAKLib normally does this itself, from a hook on
        /// <c>ItemDatabase.OnLoaded</c>: whatever has been registered by then
        /// is given an id and added. That hook does not always install — its
        /// module's startup can fail without saying so, and when it does, the
        /// item exists, has a network prefab, and is invisible to the game.
        /// Which is the worst of the three possible outcomes, because
        /// everything reports success.
        ///
        /// So this checks and, if needed, does the same work by hand. The id is
        /// hashed exactly the way PEAKLib hashes it — the same two strings in
        /// the same order — so an item registered this way and one registered
        /// by the library are the same item, with the same id, and a save from
        /// one is readable by the other.
        ///
        /// Returns true if the item is in the database when this returns,
        /// however it got there.
        /// </summary>
        internal static bool EnsureInDatabase()
        {
            if (!TrackerItem.Registered) return false;

            Item item = TrackerItem.Prefab.GetComponent<Item>();
            if (item == null) return false;

            ItemDatabase database = Zorro.Core.SingletonAsset<ItemDatabase>.Instance;
            if (database == null || database.itemLookup == null) return false;

            if (item.itemID != 0 && database.itemLookup.ContainsKey(item.itemID)) return true;

            ushort id = HashId(item);

            // The same walk PEAKLib does on a collision, and for the same
            // reason: two mods can hash to one number, and the loser would
            // otherwise silently replace the winner.
            ushort start = id;
            while (database.itemLookup.ContainsKey(id))
            {
                id++;
                if (id == start)
                {
                    Plugin.Logger.LogError("Tracker: every item id is taken; the device cannot be registered.");
                    return false;
                }
            }

            item.itemID = id;
            database.Objects.Add(item);
            database.itemLookup.Add(id, item);

            // The spawn weights are worked out once, from whatever the database
            // held at the time, and cached. Adding an item afterwards without
            // clearing that leaves it in the database and out of every chest —
            // registered, findable by name, and never actually found.
            LootData.AllSpawnWeightData = null;

            Plugin.Logger.LogWarning(
                $"Tracker: PEAKLib did not add the device to the item database, so it was added " +
                $"directly, with id {id}.");

            return true;
        }

        private static ushort HashId(Item item)
        {
            // PEAKLib hashes the mod id followed by the item's name - and by
            // then the name already carries the mod id as a prefix, because
            // registering the network prefab renamed it. Mirrored exactly
            // rather than tidied, so both routes produce the same number.
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes((_modId ?? string.Empty) + item.name));

                return System.BitConverter.ToUInt16(hash, 0);
            }
        }
    }
}
