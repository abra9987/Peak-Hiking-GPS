using UnityEngine;
using UnityEngine.Rendering;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Marks every mesh as GPU-readable before the game draws it.
    ///
    /// Unity only honours <see cref="Mesh.indexBufferTarget"/> while a mesh has
    /// yet to be uploaded; asking afterwards leaves GetIndexBuffer returning
    /// null forever. That is precisely what happened to the terrain shells —
    /// "Beach" reports 2880 indices and hands back no buffer — so the ground
    /// went missing from the export while the rocks came through.
    ///
    /// Running during the loading screen catches meshes in the window between
    /// being loaded and being rendered, which is the only moment the flag can
    /// still take effect.
    /// </summary>
    internal static class MeshPrimer
    {
        private const float IntervalSeconds = 0.5f;

        private static float _nextRun;
        private static int _primed;

        public static int Primed => _primed;

        /// <summary>Call every frame; it throttles itself.</summary>
        public static void Tick()
        {
            // While a scene is streaming in, meshes appear and are uploaded
            // within a frame or two of each other, so the only chance of
            // catching one in between is to look every frame.
            bool loading = LoadingScreenHandler.loading;

            if (!loading)
            {
                if (Time.realtimeSinceStartup < _nextRun) return;
                _nextRun = Time.realtimeSinceStartup + IntervalSeconds;
            }

            Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            int primed = 0;

            for (int i = 0; i < meshes.Length; i++)
            {
                Mesh mesh = meshes[i];
                if (mesh == null || mesh.isReadable) continue;

                // Assigning is cheap and idempotent; the flags only matter for
                // meshes not yet uploaded, and there is no way to ask which
                // those are.
                if ((mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) != 0) continue;

                try
                {
                    mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                    mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                    primed++;
                }
                catch
                {
                    // Some meshes refuse; nothing to do but carry on.
                }
            }

            if (primed > 0)
            {
                _primed += primed;
                Plugin.Logger.LogInfo($"Primed {primed} meshes for GPU readback ({_primed} total).");
            }
        }
    }
}
