using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakMapInteractive.Capture
{
    /// <summary>Vertex and index data pulled off one mesh.</summary>
    internal sealed class MeshData
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Color32[] Colors;   // null when the mesh carries none
        public int[] Indices;
    }

    /// <summary>
    /// Reads geometry out of a mesh, including meshes the mesh API refuses.
    ///
    /// Most of PEAK's terrain ships Read/Write Disabled — "Snow", "Beach",
    /// "Forest" and the rock formations all throw from mesh.vertices. Their GPU
    /// buffers are still reachable, so the geometry is not actually locked
    /// away: it just has to be read as bytes and decoded against the vertex
    /// layout the mesh reports.
    ///
    /// This is what makes exporting the real model possible rather than
    /// settling for a heightfield, which by construction cannot hold a cave or
    /// an overhang.
    /// </summary>
    internal static class MeshReader
    {
        /// <summary>Geometry lost to meshes that refuse to be read, for this segment.</summary>
        public static long LostTriangles;
        public static int LostMeshes;

        public static void ResetLostCounters()
        {
            LostTriangles = 0;
            LostMeshes = 0;
        }

        public static MeshData Read(Mesh mesh)
        {
            if (mesh == null) return null;

            return mesh.isReadable ? ReadDirect(mesh) : ReadFromGpu(mesh);
        }

        private static MeshData ReadDirect(Mesh mesh)
        {
            return new MeshData
            {
                Positions = mesh.vertices,
                Normals = mesh.normals,
                Colors = mesh.colors32 != null && mesh.colors32.Length == mesh.vertexCount ? mesh.colors32 : null,
                Indices = mesh.triangles
            };
        }

        private static MeshData ReadFromGpu(Mesh mesh)
        {
            GraphicsBuffer vertexBuffer = null;
            GraphicsBuffer indexBuffer = null;

            try
            {
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

                vertexBuffer = mesh.GetVertexBuffer(0);
                indexBuffer = mesh.GetIndexBuffer();
                if (vertexBuffer == null || indexBuffer == null) return null;

                byte[] vertexBytes = ReadBytes(vertexBuffer);
                byte[] indexBytes = ReadBytes(indexBuffer);
                if (vertexBytes == null || indexBytes == null) return null;

                var data = new MeshData
                {
                    Positions = new Vector3[mesh.vertexCount],
                    Normals = new Vector3[mesh.vertexCount],
                    Indices = DecodeIndices(mesh, indexBytes)
                };

                DecodeVertices(mesh, vertexBytes, data);
                return data;
            }
            catch (Exception e)
            {
                Diagnose(mesh, e);
                return null;
            }
            finally
            {
                vertexBuffer?.Dispose();
                indexBuffer?.Dispose();
            }
        }

        /// <summary>
        /// Says why a mesh could not be read, in enough detail to act on.
        ///
        /// The terrain shells - "Beach", "Forest", "ground" - are exactly the
        /// meshes that fail, and they are the ones the map most needs. Static
        /// batching is the usual reason an index buffer disappears while the
        /// vertex buffer survives, so that is reported alongside whether a
        /// collider carries the same geometry in readable form.
        /// </summary>
        private static void Diagnose(Mesh mesh, Exception error)
        {
            long indexCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) indexCount += (long)mesh.GetIndexCount(i);

            LostTriangles += indexCount / 3;
            LostMeshes++;

            if (LostMeshes <= 5)
            {
                Plugin.Logger.LogWarning(
                    $"  cannot read '{mesh.name}': {error.Message} " +
                    $"(submeshes={mesh.subMeshCount}, indices={indexCount}, format={mesh.indexFormat}, " +
                    $"verts={mesh.vertexCount})");
            }
        }

        private static byte[] ReadBytes(GraphicsBuffer buffer)
        {
            int total = buffer.count * buffer.stride;
            if (total <= 0) return null;

            // A Raw buffer is addressed as 32-bit words.
            var words = new uint[(total + 3) / 4];
            buffer.GetData(words);

            var bytes = new byte[total];
            Buffer.BlockCopy(words, 0, bytes, 0, total);
            return bytes;
        }

        /// <summary>
        /// Decodes the index buffer through the submesh table.
        ///
        /// Reading the buffer as one flat run is wrong whenever a submesh
        /// carries a non-zero baseVertex: every triangle then points at the
        /// wrong vertices and the object comes out as noise shaped vaguely
        /// like the original. The table gives the offset each submesh's
        /// indices are relative to.
        /// </summary>
        private static int[] DecodeIndices(Mesh mesh, byte[] bytes)
        {
            bool sixteenBit = mesh.indexFormat == IndexFormat.UInt16;
            int stride = sixteenBit ? 2 : 4;
            int available = bytes.Length / stride;

            var indices = new List<int>(available);

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                SubMeshDescriptor descriptor = mesh.GetSubMesh(sub);
                if (descriptor.topology != MeshTopology.Triangles) continue;

                int end = descriptor.indexStart + descriptor.indexCount;
                if (end > available) end = available;

                for (int i = descriptor.indexStart; i < end; i++)
                {
                    int value = sixteenBit
                        ? BitConverter.ToUInt16(bytes, i * 2)
                        : (int)BitConverter.ToUInt32(bytes, i * 4);

                    indices.Add(value + descriptor.baseVertex);
                }
            }

            return indices.ToArray();
        }

        /// <summary>
        /// Walks the interleaved vertex stream using the layout the mesh
        /// reports. Positions are always Float32x3; normals and colours vary,
        /// so both encodings actually seen in this game are handled and
        /// anything else is skipped rather than guessed at.
        /// </summary>
        private static void DecodeVertices(Mesh mesh, byte[] bytes, MeshData data)
        {
            VertexAttributeDescriptor[] attributes = mesh.GetVertexAttributes();

            int stride = 0;
            int positionOffset = -1;
            int normalOffset = -1, normalDim = 0;
            VertexAttributeFormat normalFormat = VertexAttributeFormat.Float32;
            int colorOffset = -1;
            VertexAttributeFormat colorFormat = VertexAttributeFormat.UNorm8;

            foreach (VertexAttributeDescriptor attribute in attributes)
            {
                if (attribute.stream != 0) continue;

                int size = FormatSize(attribute.format) * attribute.dimension;

                switch (attribute.attribute)
                {
                    case VertexAttribute.Position: positionOffset = stride; break;
                    case VertexAttribute.Normal:
                        normalOffset = stride;
                        normalFormat = attribute.format;
                        normalDim = attribute.dimension;
                        break;
                    case VertexAttribute.Color:
                        colorOffset = stride;
                        colorFormat = attribute.format;
                        break;
                }

                stride += size;
            }

            if (positionOffset < 0 || stride == 0) return;
            if (colorOffset >= 0) data.Colors = new Color32[mesh.vertexCount];

            for (int v = 0; v < mesh.vertexCount; v++)
            {
                int baseOffset = v * stride;
                if (baseOffset + stride > bytes.Length) break;

                int p = baseOffset + positionOffset;
                data.Positions[v] = new Vector3(
                    BitConverter.ToSingle(bytes, p),
                    BitConverter.ToSingle(bytes, p + 4),
                    BitConverter.ToSingle(bytes, p + 8));

                if (normalOffset >= 0)
                    data.Normals[v] = ReadVector(bytes, baseOffset + normalOffset, normalFormat, normalDim);

                if (colorOffset >= 0)
                    data.Colors[v] = ReadColor(bytes, baseOffset + colorOffset, colorFormat);
            }
        }

        private static Vector3 ReadVector(byte[] bytes, int offset, VertexAttributeFormat format, int dimension)
        {
            if (dimension < 3) return Vector3.up;

            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    return new Vector3(
                        BitConverter.ToSingle(bytes, offset),
                        BitConverter.ToSingle(bytes, offset + 4),
                        BitConverter.ToSingle(bytes, offset + 8));

                case VertexAttributeFormat.Float16:
                    return new Vector3(
                        Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset)),
                        Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset + 2)),
                        Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset + 4)));

                default:
                    return Vector3.up;
            }
        }

        private static Color32 ReadColor(byte[] bytes, int offset, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.UNorm8:
                    return new Color32(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);

                case VertexAttributeFormat.Float32:
                    return new Color32(
                        (byte)(Mathf.Clamp01(BitConverter.ToSingle(bytes, offset)) * 255f),
                        (byte)(Mathf.Clamp01(BitConverter.ToSingle(bytes, offset + 4)) * 255f),
                        (byte)(Mathf.Clamp01(BitConverter.ToSingle(bytes, offset + 8)) * 255f),
                        (byte)(Mathf.Clamp01(BitConverter.ToSingle(bytes, offset + 12)) * 255f));

                case VertexAttributeFormat.Float16:
                    return new Color32(
                        (byte)(Mathf.Clamp01(Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset))) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset + 2))) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset + 4))) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, offset + 6))) * 255f));

                default:
                    return new Color32(255, 255, 255, 255);
            }
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;

                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;

                default:
                    return 1;   // UNorm8, SNorm8, UInt8, SInt8
            }
        }
    }
}
