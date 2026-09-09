using System;
using System.Collections.Generic;

namespace PeakMapInteractive.Collect
{
    /// <summary>Marker groups the web client can toggle independently.</summary>
    internal static class MarkerKind
    {
        public const string Luggage = "luggage";
        public const string Belltower = "belltower";
        public const string Animal = "animal";
        public const string Amulet = "amulet";
        public const string Tomb = "tomb";
        public const string Statue = "statue";
        public const string Campfire = "campfire";
        public const string Misc = "misc";
    }

    /// <summary>
    /// Maps things found in the scene to marker kinds.
    ///
    /// Matching is by *name*, resolved at runtime, rather than by compile-time
    /// references to game types. That costs a dictionary lookup per component
    /// and buys two things: the plugin still loads when a game update renames
    /// or removes a type, and adding new content is a one-line change here
    /// rather than a new Harmony patch class.
    /// </summary>
    internal static class MarkerRegistry
    {
        /// <summary>Component type name (no namespace) to marker kind.</summary>
        private static readonly Dictionary<string, string> ByComponentType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // PEAK ships many Luggage* classes (LuggageBeach, LuggageCaldera,
                // LuggageGloom, LuggageMesa, LuggageRoots, LuggageTundra, ...).
                // They appear to be one visual chest with per-biome loot tables
                // rather than distinct objects. The exact class is exported
                // verbatim regardless: which loot pool a chest draws from is
                // information no existing map surfaces, and collapsing the
                // classes here would destroy it irreversibly. Grouping them
                // under one icon is a presentation choice and lives in the web
                // client's taxonomy, not in the capture.
                { "Luggage", MarkerKind.Luggage },
                { "Capybara", MarkerKind.Animal },
                { "Beehive", MarkerKind.Animal },
                { "EarlyWorm", MarkerKind.Animal },
                { "Antlion", MarkerKind.Animal },
                { "AmuletBase", MarkerKind.Amulet },
                { "ScoutAmulet", MarkerKind.Amulet },
                { "Bellcircle", MarkerKind.Belltower },
                { "GloomSafeZone", MarkerKind.Belltower },
            };

        /// <summary>
        /// GameObject name fragments, checked when no component matched.
        /// Ordered: the first fragment contained in the name wins.
        /// </summary>
        private static readonly (string Fragment, string Kind)[] ByObjectName =
        {
            ("scout statue", MarkerKind.Statue),
            ("scouteffigy", MarkerKind.Statue),
            ("luggage", MarkerKind.Luggage),
            ("amulet", MarkerKind.Amulet),
            ("belltower", MarkerKind.Belltower),
            ("bellcircle", MarkerKind.Belltower),
            ("tomb", MarkerKind.Tomb),
            ("capybara", MarkerKind.Animal),
            ("beehive", MarkerKind.Animal),
            ("antlion", MarkerKind.Animal),
            ("earlyworm", MarkerKind.Animal),
            ("campfire", MarkerKind.Campfire),
        };

        public static bool TryMatchComponent(string typeName, out string kind)
            => ByComponentType.TryGetValue(typeName, out kind);

        public static bool TryMatchObjectName(string objectName, out string kind)
        {
            string lowered = objectName.ToLowerInvariant();
            for (int i = 0; i < ByObjectName.Length; i++)
            {
                if (lowered.Contains(ByObjectName[i].Fragment))
                {
                    kind = ByObjectName[i].Kind;
                    return true;
                }
            }

            kind = null;
            return false;
        }

        /// <summary>Strips Unity's instantiation suffix from an object name.</summary>
        public static string CleanTypeName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return "Unknown";

            int clone = objectName.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) objectName = objectName.Substring(0, clone);

            return objectName.Trim();
        }
    }
}
