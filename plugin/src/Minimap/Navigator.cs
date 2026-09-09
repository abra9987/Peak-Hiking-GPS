using System.IO;
using System.Reflection;
using UnityEngine;

namespace PeakMapInteractive.Minimap
{
    /// <summary>
    /// The handheld navigator the map is shown inside: its artwork, and where
    /// the screen and the buttons sit on it.
    ///
    /// The map used to be a bordered panel in the corner, which reads as
    /// something bolted over the game rather than as something from it. This is
    /// a drawn device instead — a rugged orange GPS unit with a screen, three
    /// buttons and an antenna — and the map is what is on its screen.
    ///
    /// The artwork is ours, drawn for this mod, so it ships inside the assembly
    /// and carries no licence problem. That is the whole reason the icons are
    /// photographed at runtime rather than shipped: PEAK's art stays in PEAK,
    /// and everything in this file is not PEAK's.
    /// </summary>
    internal static class Navigator
    {
        /// <summary>
        /// The case, in the pixels it was drawn at. Every measurement below is
        /// in those pixels, read off the alpha channel: a hole is where the
        /// drawing is transparent, so these are facts about the artwork rather
        /// than numbers somebody chose to match it.
        ///
        /// The drawing arrived on a 1254-square canvas with the case floating
        /// in the middle of it. It is cropped to the case itself, because a
        /// fifth of that square was empty margin and the map is what the space
        /// is wanted for.
        /// </summary>
        private const float Width = 999f;
        private const float Height = 1216f;

        private static readonly Rect Screen = FromPixels(182f, 295f, 817f, 895f);

        private static readonly Rect[] Buttons =
        {
            FromPixels(173f, 971f, 351f, 1114f),
            FromPixels(410f, 971f, 589f, 1114f),
            FromPixels(647f, 971f, 826f, 1114f)
        };

        /// <summary>How much wider the case is than it is tall.</summary>
        internal static float Aspect => Width / Height;

        /// <summary>Where the map goes, as a fraction of the artwork.</summary>
        internal static Rect ScreenArea => Screen;

        /// <summary>How much wider the screen is than it is tall, on screen.</summary>
        internal static float ScreenAspect => Screen.width * Aspect / Screen.height;

        internal static Rect ButtonArea(int index) => Buttons[Mathf.Clamp(index, 0, Buttons.Length - 1)];

        internal static int ButtonCount => Buttons.Length;

        /// <summary>
        /// A rectangle measured in artwork pixels, turned into the fractions a
        /// <see cref="RectTransform"/> anchors by. Image coordinates run
        /// downwards from the top and Unity's run upwards from the bottom, so
        /// the vertical pair is flipped as well as scaled.
        /// </summary>
        private static Rect FromPixels(float left, float top, float right, float bottom)
            => Rect.MinMaxRect(
                left / Width,
                1f - bottom / Height,
                right / Width,
                1f - top / Height);

        // --- artwork ---------------------------------------------------------

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _art =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        internal static Sprite Body => Load("body.png");
        internal static Sprite Glass => Load("glass-overlay.png");

        /// <summary>
        /// The face of one button. Left zooms out, right zooms in, and the
        /// middle one is drawn as a map pin.
        /// </summary>
        internal static Sprite Button(int index)
        {
            switch (index)
            {
                case 0: return Load("zoom_out_0.png");
                case 1: return Load("tilt_0.png");
                default: return Load("zoom_in_0.png");
            }
        }

        /// <summary>True once the artwork is there to be drawn.</summary>
        internal static bool Available => Body != null;

        /// <summary>
        /// Pulls one drawing out of the assembly it was built into. Nothing is
        /// read from disk, so there is no folder for a user to lose and no
        /// loose file for a mod manager to miss.
        /// </summary>
        private static Sprite Load(string name)
        {
            if (_art.TryGetValue(name, out Sprite cached)) return cached;

            _art[name] = null;

            Assembly assembly = typeof(Navigator).Assembly;
            string resource = null;

            foreach (string candidate in assembly.GetManifestResourceNames())
            {
                if (!candidate.EndsWith(name, System.StringComparison.OrdinalIgnoreCase)) continue;

                resource = candidate;
                break;
            }

            if (resource == null)
            {
                Plugin.Logger.LogWarning($"Navigator: '{name}' is not in the assembly.");
                return null;
            }

            byte[] png;

            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return null;

                png = new byte[stream.Length];
                stream.Read(png, 0, png.Length);
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
            {
                name = "PeakMapInteractive_" + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(png))
            {
                Plugin.Logger.LogWarning($"Navigator: '{name}' could not be decoded.");
                Object.Destroy(texture);
                return null;
            }

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);

            _art[name] = sprite;
            return sprite;
        }
    }
}
