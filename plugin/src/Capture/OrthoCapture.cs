using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Photographs a segment straight down, using the game's own camera.
    ///
    /// Borrowing the existing camera rather than creating one is the whole
    /// trick. A freshly created camera — enabled, base render type, with a
    /// UniversalRenderer and an accepted SubmitRenderRequest — draws nothing at
    /// all in this game's pipeline, at any resolution or render path. The
    /// camera the game already renders through works first time, because
    /// whatever registration URP needs has already happened to it.
    ///
    /// Everything touched is restored afterwards, so a capture leaves the
    /// running game exactly as it found it.
    ///
    /// Orthographic projection is the reason the result is usable as a map:
    /// world XZ maps to texture UV by an exact linear transform, so markers
    /// land where they belong with no per-biome calibration.
    /// </summary>
    internal static class OrthoCapture
    {
        public static IEnumerator Capture(CaptureFrame frame, int resolution, int cullingMask, Action<Texture2D> onDone)
        {
            Camera cam = FindGameCamera();
            if (cam == null)
            {
                Plugin.Logger.LogWarning("  no game camera to borrow; skipping orthophoto.");
                onDone?.Invoke(null);
                yield break;
            }

            // Render to the screen and read the back buffer, rather than to a
            // render texture.
            //
            // Every render-texture route came back black: a purpose-built
            // camera, the game's own camera, Camera.Render, URP render
            // requests, 1024 and 4096, window visible and hidden. The back
            // buffer is the one surface this game demonstrably draws into, and
            // ScreenCapture reads exactly what the player would see — which is
            // also the point, since the whole value of a photo is that it
            // matches the game.
            int side = Mathf.Min(1024, resolution);
            int superSize = Mathf.Max(1, Mathf.RoundToInt((float)resolution / side));

            int savedWidth = Screen.width;
            int savedHeight = Screen.height;
            FullScreenMode savedMode = Screen.fullScreenMode;

            var saved = CameraState.Save(cam);

            // The camera is driven every LateUpdate by MainCameraMovement, which
            // would put it back on the player before the frame is drawn.
            List<Behaviour> suspended = SuspendDrivers(cam);

            bool savedFog = RenderSettings.fog;
            Texture2D texture = null;

            RenderSettings.fog = false;

            // A square window so the shot has no aspect distortion, and so
            // orthographicSize covers exactly the captured frame.
            Screen.SetResolution(side, side, FullScreenMode.Windowed);

            cam.transform.position = new Vector3(frame.CenterX, frame.MaxY + 10f, frame.CenterZ);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = frame.SizeZ * 0.5f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = frame.Height + 400f;
            cam.ResetAspect();
            cam.cullingMask = WorldMask(cam.cullingMask);

            // The white haze over the summit is not geometry: it survives any
            // culling mask because it is drawn as post-processing. Turning post
            // off for the borrowed camera is what finally reveals the ground.
            var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            bool savedPost = camData != null && camData.renderPostProcessing;
            if (camData != null) camData.renderPostProcessing = false;

            // A resolution change lands at the end of a frame, and the camera
            // needs one more to be drawn where it was just put.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            try
            {
                // superSize re-renders the scene at a multiple of the window
                // size, so a modest window still yields a detailed map.
                texture = ScreenCapture.CaptureScreenshotAsTexture(superSize);

                if (texture == null)
                {
                    Plugin.Logger.LogWarning("  screen capture returned nothing.");
                }
                else if (IsUniform(texture, out Color32 uniform))
                {
                    Plugin.Logger.LogWarning(
                        $"  orthophoto is uniform r{uniform.r} g{uniform.g} b{uniform.b}; reporting no photo.");
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                }
                else
                {
                    Plugin.Logger.LogInfo($"  orthophoto {texture.width}x{texture.height} captured.");
                }

                onDone?.Invoke(texture);
                texture = null; // ownership handed to the callback
            }
            finally
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);

                RenderSettings.fog = savedFog;
                if (camData != null) camData.renderPostProcessing = savedPost;
                saved.Restore(cam);
                Screen.SetResolution(savedWidth, savedHeight, savedMode);

                foreach (Behaviour behaviour in suspended)
                    if (behaviour != null) behaviour.enabled = true;
            }
        }

        /// <summary>
        /// Layers that make up the ground, and nothing else.
        ///
        /// Looking straight down from above the summit means looking through
        /// the cloud deck: the first successful capture of the Alpine segment
        /// was a photograph of cloud with a hole in it. Restricting the camera
        /// to surface layers removes clouds, weather and effects while keeping
        /// the terrain exactly as the game renders it.
        ///
        /// Falls back to the camera's own mask if the game ever renames these.
        /// </summary>
        private static int WorldMask(int fallback)
        {
            // Terrain and Map only. Clouds sit on Default alongside props, and
            // including Default photographed the cloud deck instead of the
            // mountain under it.
            string[] wanted = { "Terrain", "Map" };
            int mask = 0;

            foreach (string name in wanted)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0) mask |= 1 << layer;
            }

            if (mask == 0)
            {
                Plugin.Logger.LogWarning("  no surface layers resolved; keeping the camera's own mask.");
                return fallback;
            }

            return mask;
        }

        /// <summary>
        /// Lists the biggest renderers in the scene, with layer and altitude.
        ///
        /// A white haze kept covering the summit through every culling mask and
        /// with post-processing off. Naming what is actually up there beats
        /// another guess -- the same approach that identified the water plane.
        /// </summary>
        public static void LogLargeRenderers(float minSpan)
        {
            var seen = new List<string>();

            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;

                Bounds b = r.bounds;
                if (b.size.x < minSpan && b.size.z < minSpan) continue;

                seen.Add($"'{r.gameObject.name}' layer={LayerMask.LayerToName(r.gameObject.layer)}" +
                         $"({r.gameObject.layer}) y={b.center.y:F0} size={b.size.x:F0}x{b.size.y:F0}x{b.size.z:F0}" +
                         $" mat={r.sharedMaterial?.name ?? "none"}");

                if (seen.Count >= 25) break;
            }

            Plugin.Logger.LogInfo($"Large renderers (span >= {minSpan:F0}):");
            foreach (string line in seen) Plugin.Logger.LogInfo("  " + line);
        }

        /// <summary>Writes the project's layer names once, so masks can be reasoned about.</summary>
        public static void LogLayers()
        {
            var named = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name)) named.Add($"{i}:{name}");
            }

            Plugin.Logger.LogInfo("Layers: " + string.Join(", ", named));
        }

        /// <summary>
        /// Blocks until the game is actually drawing the world.
        ///
        /// Dismissing the loading screen does not make the world appear the
        /// same frame; the first segments of a capture were photographed
        /// during that gap and came out black while later ones succeeded. A
        /// small screenshot is the cheapest honest test of "is anything being
        /// drawn", and it costs a few frames once per capture.
        /// </summary>
        public static IEnumerator WaitForWorldToRender(float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();

                Texture2D probe = ScreenCapture.CaptureScreenshotAsTexture();
                if (probe == null) continue;

                bool blank = IsUniform(probe, out _);
                UnityEngine.Object.Destroy(probe);

                if (!blank)
                {
                    Plugin.Logger.LogInfo("World is rendering; starting capture.");
                    yield break;
                }
            }

            Plugin.Logger.LogWarning(
                $"World still not rendering after {timeoutSeconds:F0}s; capturing anyway.");
        }

        /// <summary>
        /// The camera the game renders through. <see cref="MainCamera"/> is
        /// preferred because it is unambiguous; Camera.main is the fallback for
        /// scenes where it has not been set up.
        /// </summary>
        private static Camera FindGameCamera()
        {
            // MainCamera.cam is internal to the game assembly, so take the
            // component off the same object rather than the field.
            Camera cam = MainCamera.instance != null
                ? MainCamera.instance.GetComponent<Camera>()
                : null;

            if (cam != null) return cam;
            if (Camera.main != null) return Camera.main;

            foreach (Camera candidate in Camera.allCameras)
                if (candidate.enabled && candidate.targetTexture == null) return candidate;

            return null;
        }

        /// <summary>
        /// Turns off everything that would move or reconfigure the camera while
        /// it is borrowed, and reports what was disabled so it can be put back.
        /// </summary>
        private static List<Behaviour> SuspendDrivers(Camera cam)
        {
            var suspended = new List<Behaviour>();

            foreach (Behaviour behaviour in cam.GetComponents<Behaviour>())
            {
                if (behaviour == null || behaviour is Camera || !behaviour.enabled) continue;

                behaviour.enabled = false;
                suspended.Add(behaviour);
            }

            // The mover often lives beside the camera rather than on it.
            foreach (var mover in UnityEngine.Object.FindObjectsByType<MainCameraMovement>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mover == null || !mover.enabled) continue;

                mover.enabled = false;
                suspended.Add(mover);
            }

            // Volumes carry the fog and colour grading that would otherwise
            // paint the map white from above.
            foreach (var volume in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (volume == null || !volume.enabled) continue;

                volume.enabled = false;
                suspended.Add(volume);
            }

            // Screen-space canvases draw over everything no matter where the
            // camera points or what it culls. The first back-buffer capture
            // came back as a photograph of the loading screen.
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.enabled) continue;

                canvas.enabled = false;
                suspended.Add(canvas);
            }

            return suspended;
        }


        /// <summary>
        /// True when every sampled pixel is the same colour, the only visible
        /// symptom of a capture that silently produced nothing.
        /// </summary>
        private static bool IsUniform(Texture2D texture, out Color32 colour)
        {
            const int step = 64;
            colour = texture.GetPixel(0, 0);

            for (int y = 0; y < texture.height; y += step)
            {
                for (int x = 0; x < texture.width; x += step)
                {
                    Color32 c = texture.GetPixel(x, y);
                    if (c.r != colour.r || c.g != colour.g || c.b != colour.b) return false;
                }
            }

            return true;
        }

        /// <summary>Everything the capture changes on the borrowed camera.</summary>
        private readonly struct CameraState
        {
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly bool _orthographic;
            private readonly float _orthographicSize;
            private readonly float _aspect;
            private readonly bool _usePhysicalProperties;
            private readonly float _near;
            private readonly float _far;
            private readonly RenderTexture _target;

            private CameraState(Camera cam)
            {
                _position = cam.transform.position;
                _rotation = cam.transform.rotation;
                _orthographic = cam.orthographic;
                _orthographicSize = cam.orthographicSize;
                _aspect = cam.aspect;
                _usePhysicalProperties = cam.usePhysicalProperties;
                _near = cam.nearClipPlane;
                _far = cam.farClipPlane;
                _target = cam.targetTexture;
            }

            public static CameraState Save(Camera cam) => new CameraState(cam);

            public void Restore(Camera cam)
            {
                if (cam == null) return;

                cam.targetTexture = _target;
                cam.orthographic = _orthographic;
                cam.orthographicSize = _orthographicSize;
                cam.usePhysicalProperties = _usePhysicalProperties;
                cam.nearClipPlane = _near;
                cam.farClipPlane = _far;
                // ResetAspect puts the camera back on the screen's aspect
                // rather than the square one the capture forced.
                cam.ResetAspect();
                cam.aspect = _aspect;
                cam.transform.SetPositionAndRotation(_position, _rotation);
            }
        }
    }
}
