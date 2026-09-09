using System.Collections.Generic;
using UnityEngine;
using Zorro.Core;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Brings one map segment into a state where it can be measured: fully
    /// streamed in, with the walls and set dressing that would occlude a
    /// top-down view temporarily hidden.
    ///
    /// Every change is recorded and reverted by <see cref="Restore"/>. The
    /// reference project destroys occluders outright, which is fine for a
    /// process that quits immediately afterwards but makes the plugin unusable
    /// during normal play and impossible to run twice in one session.
    /// </summary>
    internal sealed class SegmentPreparer
    {
        private readonly List<GameObject> _activated = new List<GameObject>();
        private readonly List<GameObject> _deactivated = new List<GameObject>();
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();

        /// <summary>
        /// Object name fragments that sit above the terrain and would otherwise
        /// be all a downward ray or camera ever sees. Matched case-insensitively.
        /// </summary>
        private static readonly string[] OccluderNameFragments =
        {
            "gloom temple",
            "volcanomodel",
            "rock_round",
            "rockfinal",
            "cloud",
            "fog",
        };

        public GameObject Root { get; private set; }

        public void Prepare(GameObject segmentRoot, GameObject campfire, GameObject wallNext, GameObject wallPrevious)
        {
            Root = segmentRoot;

            SetActive(segmentRoot, true);

            // Sub-steps are the streaming units inside a segment; without them
            // the far half of a biome is simply not in the scene yet.
            if (segmentRoot != null)
            {
                foreach (var substep in segmentRoot.GetComponentsInChildren<EnablingSubstep>(includeInactive: true))
                {
                    if (substep != null) SetActive(substep.gameObject, true);
                }
            }

            SetActive(campfire, true);
            SetActive(wallNext, false);
            SetActive(wallPrevious, false);

            HideOccluders(segmentRoot);
        }

        /// <summary>
        /// Disables renderers whose object name marks them as overhead set
        /// dressing. Renderers rather than whole GameObjects, so colliders stay
        /// intact and the surface underneath still measures correctly.
        /// </summary>
        private void HideOccluders(GameObject segmentRoot)
        {
            if (segmentRoot == null) return;

            foreach (var renderer in segmentRoot.GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (renderer == null || !renderer.enabled) continue;

                string name = renderer.gameObject.name.ToLowerInvariant();
                for (int i = 0; i < OccluderNameFragments.Length; i++)
                {
                    if (!name.Contains(OccluderNameFragments[i])) continue;

                    renderer.enabled = false;
                    _hiddenRenderers.Add(renderer);
                    break;
                }
            }
        }

        /// <summary>Asks every spawner in the segment to place its items.</summary>
        public void SpawnItems()
        {
            if (Root == null) return;

            foreach (var spawner in Root.GetComponentsInChildren<ISpawner>(includeInactive: true))
            {
                try { spawner.TrySpawnItems(); }
                catch (System.Exception e)
                {
                    Plugin.Logger.LogWarning($"Spawner {spawner} failed: {e.Message}");
                }
            }
        }

        public void Restore()
        {
            foreach (var renderer in _hiddenRenderers)
                if (renderer != null) renderer.enabled = true;

            foreach (var go in _activated)
                if (go != null) go.SetActive(false);

            foreach (var go in _deactivated)
                if (go != null) go.SetActive(true);

            _hiddenRenderers.Clear();
            _activated.Clear();
            _deactivated.Clear();
            Root = null;
        }

        private void SetActive(GameObject go, bool active)
        {
            if (go == null || go.activeSelf == active) return;

            go.SetActive(active);
            (active ? _activated : _deactivated).Add(go);
        }
    }
}
