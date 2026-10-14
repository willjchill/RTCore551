// RTCore551 - Task 1: fixed-point helpers shared by the whole export pipeline.
// No UnityEngine dependency on purpose, so this file can be unit-tested with plain dotnet.
using System;

namespace RTCore
{
    /// <summary>
    /// Fixed-point conversion. World positions use Q16.16 (signed 32-bit, 16 fractional bits,
    /// range about +/-32768). Camera pixel-step vectors use Q4.28 so that x * stepX stays
    /// accurate across a few hundred pixels.
    /// </summary>
    public static class Fx
    {
        public const int PosFracBits = 16;   // world positions, light positions, AABBs
        public const int CamFracBits = 28;   // camera ray-direction vectors

        public static int ToFixed(double value, int fracBits = PosFracBits)
        {
            double scaled = Math.Round(value * (1L << fracBits), MidpointRounding.AwayFromZero);
            if (scaled > int.MaxValue || scaled < int.MinValue)
                throw new OverflowException(
                    "Value " + value + " does not fit Q" + (32 - fracBits) + "." + fracBits +
                    ". Scale the scene down so |coordinates| stay below " + (1L << (31 - fracBits)) + ".");
            return (int)scaled;
        }

        public static double ToDouble(int fixedValue, int fracBits = PosFracBits)
        {
            return fixedValue / (double)(1L << fracBits);
        }

        public static byte ToUnorm8(double v01)
        {
            double c = Math.Min(1.0, Math.Max(0.0, v01));
            return (byte)Math.Round(c * 255.0, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Integer 3-vector in Q16.16 (or Q4.28 for camera vectors).</summary>
    public struct FxVec3
    {
        public int x, y, z;

        public FxVec3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }

        public static FxVec3 FromDoubles(double x, double y, double z, int fracBits = Fx.PosFracBits)
        {
            return new FxVec3(Fx.ToFixed(x, fracBits), Fx.ToFixed(y, fracBits), Fx.ToFixed(z, fracBits));
        }

        public int this[int axis]
        {
            get { return axis == 0 ? x : (axis == 1 ? y : z); }
            set { if (axis == 0) x = value; else if (axis == 1) y = value; else z = value; }
        }

        public static FxVec3 Min(FxVec3 a, FxVec3 b)
        {
            return new FxVec3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        }

        public static FxVec3 Max(FxVec3 a, FxVec3 b)
        {
            return new FxVec3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        }

        public override string ToString()
        {
            return "(" + Fx.ToDouble(x) + ", " + Fx.ToDouble(y) + ", " + Fx.ToDouble(z) + ")";
        }
    }
}
