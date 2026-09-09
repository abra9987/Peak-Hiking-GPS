using System;
using System.Collections;
using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// Renders a segment straight down through an orthographic camera.
    ///
    /// Orthographic projection is the whole point. It makes world XZ map to
    /// texture UV by a linear transform that is exact everywhere, so markers
    /// land where they belong without any per-biome calibration. A perspective
    /// capture has parallax that varies across the frame, which is why the
    /// reference project needs a hand-tuned camera vector per level and still
    /// ends up correcting the result with a magic divisor in CSS.
    /// </summary>
    internal static class OrthoCapture
    {
        /// <summary>
        /// Captures the frame and hands the finished texture to
        /// <paramref name="onDone"/>. The caller owns the texture and must
        /// Destroy it.
        /// </summary>
        public static IEnumerator Capture(CaptureFrame frame, int resolution, int cullingMask, Action<Texture2D> onDone)
        {
            GameObject camObject = new GameObject("PeakMapInteractive_OrthoCamera");
            RenderTexture rt = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camObject.transform.position = new Vector3(frame.CenterX, frame.MaxY + 10f, frame.CenterZ);
                camObject.transform.eulerAngles = new Vector3(90f, 0f, 0f);

                Camera cam = camObject.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = frame.SizeZ * 0.5f;
                cam.aspect = frame.SizeX / frame.SizeZ;   // 1 for a squared frame
                cam.nearClipPlane = 1f;
                cam.farClipPlane = frame.Height + 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.cullingMask = cullingMask;
                cam.allowHDR = false;
                cam.allowMSAA = false;

                rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                rt.Create();

                cam.targetTexture = rt;
                cam.enabled = true;

                // URP does not support the immediate Camera.Render() path used by
                // older tooling; letting the pipeline drive the camera and reading
                // back after the frame is the supported way to capture on demand.
                yield return new WaitForEndOfFrame();

                RenderTexture.active = rt;
                texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                texture.Apply(false, false);

                onDone?.Invoke(texture);
                texture = null; // ownership handed over
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
                UnityEngine.Object.DestroyImmediate(camObject);
            }
        }
    }
}
