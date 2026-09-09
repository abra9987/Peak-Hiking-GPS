using System;
using System.Collections.Generic;
using System.Reflection;
using PeakMapInteractive.Export;
using UnityEngine;

namespace PeakMapInteractive.Collect
{
    /// <summary>
    /// Walks a prepared segment and turns everything interesting into markers.
    ///
    /// A single scan of the segment's MonoBehaviours replaces the reference
    /// project's per-type Harmony patches. Patching Awake on each item class
    /// means anything the plugin does not already know about is silently
    /// dropped, and every new item type needs a new patch class; scanning finds
    /// candidates first and only then decides what they are.
    /// </summary>
    internal sealed class MarkerCollector
    {
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly SortedDictionary<string, int> _unknownTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Type names seen but not matched, for diagnostics.</summary>
        public IReadOnlyDictionary<string, int> UnknownTypes => _unknownTypes;

        public List<MarkerDto> Collect(GameObject segmentRoot, int segmentIndex)
        {
            var markers = new List<MarkerDto>();
            if (segmentRoot == null) return markers;

            var behaviours = segmentRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                GameObject go = behaviour.gameObject;
                int id = go.GetInstanceID();

                Type type = behaviour.GetType();
                string typeName = type.Name;

                if (!MarkerRegistry.TryMatchComponent(typeName, out string kind))
                {
                    // Base classes matter: LuggageBeach and friends derive from
                    // Luggage, so a match higher in the hierarchy still counts.
                    if (!TryMatchBaseTypes(type, out kind))
                    {
                        if (!MarkerRegistry.TryMatchObjectName(go.name, out kind))
                        {
                            Note(typeName);
                            continue;
                        }
                    }
                }

                // One marker per GameObject even when several components match.
                if (!_seen.Add(id)) continue;

                Vector3 pos = go.transform.position;

                markers.Add(new MarkerDto
                {
                    Id = $"{segmentIndex}:{kind}:{(uint)id:x8}",
                    Kind = kind,
                    Type = MarkerRegistry.CleanTypeName(go.name),
                    Name = TryGetDisplayName(behaviour),
                    Pos = SnapshotWriter.Vec3(pos)
                });
            }

            return markers;
        }

        private static bool TryMatchBaseTypes(Type type, out string kind)
        {
            for (Type t = type.BaseType; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                if (MarkerRegistry.TryMatchComponent(t.Name, out kind)) return true;
            }

            kind = null;
            return false;
        }

        /// <summary>
        /// Pulls the in-game display name when the component exposes one.
        /// Reflection rather than a cast: only some item classes have GetName,
        /// and the plugin should not fail to build because a type moved.
        /// </summary>
        private static string TryGetDisplayName(MonoBehaviour behaviour)
        {
            try
            {
                MethodInfo method = behaviour.GetType().GetMethod(
                    "GetName",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                if (method != null && method.ReturnType == typeof(string))
                    return method.Invoke(behaviour, null) as string;
            }
            catch
            {
                // A display name is a nicety; never let it break a capture.
            }

            return null;
        }

        private void Note(string typeName)
        {
            _unknownTypes.TryGetValue(typeName, out int count);
            _unknownTypes[typeName] = count + 1;
        }
    }
}
