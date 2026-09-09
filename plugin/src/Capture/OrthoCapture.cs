using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        /// Clear colour. Deliberately not black: an untouched render texture is
        /// black too, so a black result could not distinguish "the camera drew
        /// nothing" from "the camera never ran" — a distinction that cost two
        /// capture runs to work out.
        /// </summary>
        private static readonly Color ClearColour = new Color(1f, 0f, 1f, 1f);

        public static IEnumerator Capture(CaptureFrame frame, int resolution, int cullingMask, Action<Texture2D> onDone)
        {
            GameObject camObject = new GameObject("PeakMapInteractive_OrthoCamera");
            camObject.transform.position = new Vector3(frame.CenterX, frame.MaxY + 10f, frame.CenterZ);
            camObject.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            Camera cam = camObject.AddComponent<Camera>();

            // URP keeps per-camera settings in a companion component. A camera
            // created at runtime does not necessarily get one, and without it
            // the pipeline ignores the camera even though it is enabled and
            // counted in Camera.allCameras.
            EnsureUrpCameraData(camObject);

            cam.orthographic = true;
            cam.orthographicSize = frame.SizeZ * 0.5f;
            cam.aspect = frame.SizeX / frame.SizeZ;   // 1 for a squared frame
            cam.nearClipPlane = 1f;
            cam.farClipPlane = frame.Height + 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ClearColour;
            cam.cullingMask = cullingMask;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;

            RenderTexture rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();

            // One frame for the scene to settle: SegmentPreparer has just
            // activated streaming sub-steps and re-enabled renderers.
            yield return null;

            // Preferred path: ask the pipeline to draw this one camera now.
            SubmitRenderRequest(cam, rt);
            Texture2D texture = ReadBack(rt, resolution);

            if (IsUniform(texture, out Color32 flat) && !IsClearColour(flat))
            {
                // The request did not draw. Fall back to letting the pipeline
                // pick the camera up during an ordinary frame.
                UnityEngine.Object.Destroy(texture);
                texture = null;

                cam.targetTexture = rt;
                cam.enabled = true;

                yield return null;
                yield return new WaitForEndOfFrame();

                texture = ReadBack(rt, resolution);
            }

            try
            {
                if (IsUniform(texture, out Color32 uniform))
                {
                    Plugin.Logger.LogWarning(IsClearColour(uniform)
                        ? "  orthophoto is the clear colour: rendered, but no geometry was in view."
                        : $"  orthophoto is uniform r{uniform.r} g{uniform.g} b{uniform.b}: the camera never rendered. " +
                          $"pipeline={GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "built-in"} " +
                          $"cameras={Camera.allCamerasCount} rtCreated={rt.IsCreated()}");

                    // Report no photograph rather than a blank one. A black
                    // JPEG in the manifest would be indistinguishable from a
                    // real capture of an unlit segment; absence is honest, and
                    // the client shades terrain from the heightfield anyway.
                    onDone?.Invoke(null);
                    yield break;
                }

                onDone?.Invoke(texture);
                texture = null; // ownership handed to the callback
            }
            finally
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);

                cam.enabled = false;
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.DestroyImmediate(camObject);
            }
        }

        private static Texture2D ReadBack(RenderTexture rt, int resolution)
        {
            RenderTexture previous = RenderTexture.active;

            try
            {
                RenderTexture.active = rt;
                var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        /// <summary>
        /// Issues URP's single-camera render request, resolved by name so the
        /// plugin neither builds nor loads against a specific pipeline version.
        /// </summary>
        private static bool SubmitRenderRequest(Camera cam, RenderTexture target)
        {
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };

                if (!RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    Plugin.Logger.LogWarning("  pipeline will not serve a single-camera render request.");
                    return false;
                }

                RenderPipeline.SubmitRenderRequest(cam, request);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"  render request failed: {e.InnerException?.Message ?? e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gives the camera URP's companion settings component and marks it a
        /// base camera.
        ///
        /// Typed rather than reflected on purpose: an earlier version resolved
        /// this by name and, when the lookup returned null, skipped the whole
        /// step in silence. The compiler checks the type instead, so the
        /// failure cannot happen unnoticed.
        /// </summary>
        private static void EnsureUrpCameraData(GameObject camObject)
        {
            var data = camObject.GetComponent<UniversalAdditionalCameraData>()
                       ?? camObject.AddComponent<UniversalAdditionalCameraData>();

            // An overlay camera renders only as part of another camera's stack
            // and is skipped entirely on its own.
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = true;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            Plugin.Logger.LogInfo($"  ortho camera: renderType={data.renderType}, scriptableRenderer={data.scriptableRenderer?.GetType().Name ?? "null"}");
        }

        private static bool IsClearColour(Color32 c) => c.r > 200 && c.g < 60 && c.b > 200;

        /// <summary>
        /// True when every sampled pixel is the same colour, which is the only
        /// visible symptom of a capture that silently produced nothing.
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
    }
}
