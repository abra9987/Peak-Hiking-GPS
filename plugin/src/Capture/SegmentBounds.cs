using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Derives a segment's real extent from its geometry.
    ///
    /// The reference project instead offsets a hardcoded camera vector from the
    /// segment campfire, which needs a special case whenever a biome does not
    /// follow the usual layout. Measuring the actual renderers has no special
    /// cases and adapts on its own when the game changes a segment.
    /// </summary>
    internal static class SegmentBounds
    {
        /// <summary>
        /// Unions the bounds of every enabled renderer under <paramref name="root"/>.
        /// Returns false when the segment has no visible geometry.
        /// </summary>
        public static bool TryCompute(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
            bool any = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled) continue;

                // Particle systems, VFX billboards and skyboxes report absurd
                // bounds that would blow the frame open. Skip anything whose
                // extent is implausible for terrain.
                Bounds rb = r.bounds;
                if (rb.size.x > 20000f || rb.size.y > 20000f || rb.size.z > 20000f) continue;
                if (rb.size.sqrMagnitude < 1e-4f) continue;

                if (!any) { bounds = rb; any = true; }
                else bounds.Encapsulate(rb);
            }

            return any;
        }
    }
}
