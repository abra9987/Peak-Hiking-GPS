using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PeakMapInteractive.Tracker
{
    /// <summary>
    /// The device as a real object: its meshes, rebuilt in memory when the
    /// game asks for them.
    ///
    /// The model is drawn in Blender and shipped as a small binary blob built
    /// into the assembly, exactly the way the navigator artwork is. The
    /// documented route for a custom item in this game is to author a prefab
    /// in the Unity editor and bake an AssetBundle, and that route was not
    /// taken: it needs the precise editor version the game was built with, and
    /// a bundle carrying shaders breaks quietly when that version drifts. A
    /// blob of vertices has no version to drift from.
    ///
    /// Everything awkward about the conversion — handedness, winding, texture
    /// origin — is done once, offline, by <c>tools/build-tracker-mesh.py</c>,
    /// which prints what it produced so a mistake is caught at a terminal
    /// rather than as an unlit silhouette in a game that says nothing. What is
    /// left here is deliberately dull: read numbers, hand them to Unity.
    /// </summary>
    internal static class TrackerModel
    {
        /// <summary>Matches the header the build script writes.</summary>
        private static readonly byte[] Magic =
            { (byte)'P', (byte)'K', (byte)'T', (byte)'R', (byte)'A', (byte)'C', (byte)'K', 1 };

        /// <summary>
        /// One mesh out of the model, under the name it has in Blender.
        ///
        /// The offset is the node's own translation. Two parts carry one — the
        /// collision box and, in later versions, anything that sits away from
        /// the case origin — and it is kept separate from the vertices so the
        /// part can be moved as a whole. A button that has to travel 0.6 mm
        /// when pressed moves its transform, not its geometry.
        /// </summary>
        internal sealed class Part
        {
            internal string Name;
            internal string Material;
            internal Vector3 Offset;
            internal Mesh Mesh;
        }

        private static List<Part> _parts;
        private static Dictionary<string, Texture2D> _textures;
        private static bool _tried;

        /// <summary>True once the model is in memory and has something in it.</summary>
        internal static bool Available
        {
            get
            {
                Load();
                return _parts != null && _parts.Count > 0;
            }
        }

        internal static IList<Part> Parts
        {
            get
            {
                Load();
                return _parts;
            }
        }

        /// <summary>
        /// One of the textures packed alongside the meshes, by the name it has
        /// in the model — the body palette, and whatever joins it later.
        /// </summary>
        internal static Texture2D Texture(string name)
        {
            Load();

            if (_textures == null) return null;

            return _textures.TryGetValue(name, out Texture2D texture) ? texture : null;
        }

        internal static Part Find(string name)
        {
            Load();

            if (_parts == null) return null;

            foreach (Part part in _parts)
            {
                if (part.Name == name) return part;
            }

            return null;
        }

        // --- reading ---------------------------------------------------------

        private static void Load()
        {
            if (_tried) return;

            _tried = true;

            byte[] blob = Resource("tracker.mesh");
            if (blob == null)
            {
                Plugin.Logger.LogWarning("Tracker: the model is not in the assembly.");
                return;
            }

            try
            {
                Read(blob);
            }
            catch (System.Exception error)
            {
                _parts = null;
                _textures = null;
                Plugin.Logger.LogError($"Tracker: the model would not read — {error.Message}");
            }
        }

        private static void Read(byte[] blob)
        {
            using (var stream = new MemoryStream(blob, writable: false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                foreach (byte expected in Magic)
                {
                    if (reader.ReadByte() != expected)
                        throw new IOException("the header does not match; rebuild tracker.mesh");
                }

                int partCount = reader.ReadInt32();
                _parts = new List<Part>(partCount);

                for (int i = 0; i < partCount; i++)
                    _parts.Add(ReadPart(reader));

                int textureCount = reader.ReadInt32();
                _textures = new Dictionary<string, Texture2D>(textureCount);

                for (int i = 0; i < textureCount; i++)
                {
                    string name = ReadString(reader);
                    byte[] png = reader.ReadBytes(reader.ReadInt32());

                    Texture2D texture = Decode(name, png);
                    if (texture != null) _textures[name] = texture;
                }
            }
        }

        private static Part ReadPart(BinaryReader reader)
        {
            var part = new Part
            {
                Name = ReadString(reader),
                Material = ReadString(reader),
                Offset = ReadVector3(reader)
            };

            int vertexCount = reader.ReadInt32();
            int indexCount = reader.ReadInt32();

            var positions = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++) positions[i] = ReadVector3(reader);

            var normals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++) normals[i] = ReadVector3(reader);

            var uvs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());

            var indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++) indices[i] = reader.ReadUInt16();

            part.Mesh = new Mesh
            {
                name = "HikingGPS_" + part.Name,
                vertices = positions,
                normals = normals,
                uv = uvs
            };

            part.Mesh.SetTriangles(indices, 0);
            part.Mesh.RecalculateBounds();

            // Deliberately left readable. The collision box is handed to a
            // MeshCollider, which reads it, and a mesh marked otherwise fails
            // there with a message that does not mention this line.

            return part;
        }

        private static Vector3 ReadVector3(BinaryReader reader)
            => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        /// <summary>
        /// A length-prefixed string. Not <see cref="BinaryReader.ReadString"/>:
        /// that expects .NET's own seven-bit-encoded length, and the build
        /// script writes a plain four-byte one.
        /// </summary>
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            return length == 0 ? string.Empty : Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        /// <summary>
        /// The body palette and anything like it.
        ///
        /// Point filtering with no mipmaps, which for this texture is not a
        /// saving but the correct reading of it: it is a grid of flat swatches,
        /// and every face samples one of them. Smooth filtering would blend
        /// across a swatch boundary and paint a seam along the edge of a face;
        /// mipmaps would, at any distance, average the whole grid into a single
        /// muddy colour and take the case with it.
        /// </summary>
        private static Texture2D Decode(string name, byte[] png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "HikingGPS_" + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            if (texture.LoadImage(png)) return texture;

            Plugin.Logger.LogWarning($"Tracker: texture '{name}' could not be decoded.");
            Object.Destroy(texture);
            return null;
        }

        private static byte[] Resource(string name)
        {
            Assembly assembly = typeof(TrackerModel).Assembly;

            foreach (string candidate in assembly.GetManifestResourceNames())
            {
                if (!candidate.EndsWith(name, System.StringComparison.OrdinalIgnoreCase)) continue;

                using (Stream stream = assembly.GetManifestResourceStream(candidate))
                {
                    if (stream == null) return null;

                    var blob = new byte[stream.Length];
                    int read = 0;

                    while (read < blob.Length)
                    {
                        int got = stream.Read(blob, read, blob.Length - read);
                        if (got <= 0) break;
                        read += got;
                    }

                    return blob;
                }
            }

            return null;
        }
    }
}
