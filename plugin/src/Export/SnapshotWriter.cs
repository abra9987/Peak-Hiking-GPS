using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace PeakMapInteractive.Export
{
    /// <summary>Serialises a snapshot and its binary payloads to disk.</summary>
    internal static class SnapshotWriter
    {
        /// <summary>
        /// Encodes a heightfield to the uint16 layout described in
        /// docs/DATA-FORMAT.md and writes it next to the manifest.
        /// Value 0 is reserved for "no data"; real heights occupy 1..65535.
        /// </summary>
        public static void WriteHeightfield(string path, float[] heights, bool[] hit, float min, float max)
        {
            int count = heights.Length;
            float range = max - min;
            // A perfectly flat segment would divide by zero; any scale works then.
            float scale = range > 1e-6f ? 65534f / range : 0f;

            byte[] buffer = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                ushort v = 0;
                if (hit[i])
                {
                    float t = (heights[i] - min) * scale;
                    int q = Mathf.Clamp((int)(t + 0.5f), 0, 65534);
                    v = (ushort)(q + 1);
                }
                buffer[i * 2] = (byte)(v & 0xFF);
                buffer[i * 2 + 1] = (byte)(v >> 8);
            }

            File.WriteAllBytes(path, buffer);
        }

        /// <summary>Writes a captured orthophoto as JPEG.</summary>
        public static void WriteAlbedo(string path, Texture2D texture, int quality = 92)
        {
            byte[] jpg = texture.EncodeToJPG(quality);
            File.WriteAllBytes(path, jpg);
        }

        public static void WriteManifest(string path, Snapshot snapshot)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include
            };

            string json = JsonConvert.SerializeObject(snapshot, settings);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public static string Timestamp() =>
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        public static float[] Vec3(Vector3 v) => new[] { v.x, v.y, v.z };
        public static float[] Vec2(float a, float b) => new[] { a, b };
    }
}
