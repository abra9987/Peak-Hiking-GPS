using UnityEngine;

namespace PeakMapInteractive.Capture
{
    /// <summary>
    /// The world-space window a segment is captured through.
    ///
    /// Both the heightfield sampler and the orthophoto camera are driven from
    /// this single struct, which is what guarantees the two are pixel-aligned
    /// and lets the web client map world XZ to texture UV with a plain linear
    /// transform instead of hand-tuned constants.
    /// </summary>
    internal readonly struct CaptureFrame
    {
        public readonly float OriginX;
        public readonly float OriginZ;
        public readonly float SizeX;
        public readonly float SizeZ;
        public readonly float MinY;
        public readonly float MaxY;

        public CaptureFrame(float originX, float originZ, float sizeX, float sizeZ, float minY, float maxY)
        {
            OriginX = originX;
            OriginZ = originZ;
            SizeX = sizeX;
            SizeZ = sizeZ;
            MinY = minY;
            MaxY = maxY;
        }

        public float CenterX => OriginX + SizeX * 0.5f;
        public float CenterZ => OriginZ + SizeZ * 0.5f;
        public float Height => MaxY - MinY;

        public Vector3 Min => new Vector3(OriginX, MinY, OriginZ);
        public Vector3 Max => new Vector3(OriginX + SizeX, MaxY, OriginZ + SizeZ);

        /// <summary>
        /// Builds a frame around <paramref name="b"/>, padded and then squared.
        ///
        /// Squaring costs a little empty margin on the narrow axis and buys
        /// something worth much more: the heightfield and the orthophoto share
        /// one resolution and one aspect, so no code anywhere needs to reason
        /// about non-uniform scaling between them.
        /// </summary>
        public static CaptureFrame FromBounds(Bounds b, float padding)
        {
            float sizeX = b.size.x + padding * 2f;
            float sizeZ = b.size.z + padding * 2f;
            float side = Mathf.Max(sizeX, sizeZ);

            float centerX = b.center.x;
            float centerZ = b.center.z;

            return new CaptureFrame(
                centerX - side * 0.5f,
                centerZ - side * 0.5f,
                side,
                side,
                b.min.y - padding,
                b.max.y + padding);
        }

        public override string ToString() =>
            $"origin=({OriginX:F1}, {OriginZ:F1}) size=({SizeX:F1} x {SizeZ:F1}) y=[{MinY:F1} .. {MaxY:F1}]";
    }
}
