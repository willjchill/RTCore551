// RTCore551 - Task 1: plain data model of the exported 3D environment.
// Everything here is already in the integer formats the FPGA consumes.
using System;
using System.Collections.Generic;

namespace RTCore
{
    /// <summary>Legacy RTS1 triangle, vertices in Q16.16. Not the raw Episode02 mesh input.</summary>
    public struct RTTriangle
    {
        public FxVec3 v0, v1, v2;
        public ushort materialId;
        public ushort flags;               // bit0 = double sided (always set for now)

        public const ushort FlagDoubleSided = 1;

        public FxVec3 BoundsMin { get { return FxVec3.Min(v0, FxVec3.Min(v1, v2)); } }
        public FxVec3 BoundsMax { get { return FxVec3.Max(v0, FxVec3.Max(v1, v2)); } }

        /// <summary>Sum of the three vertices per axis (3x centroid) in 64-bit, exact and deterministic.</summary>
        public long CentroidTimes3(int axis)
        {
            return (long)v0[axis] + v1[axis] + v2[axis];
        }

        /// <summary>True if the triangle has zero area after quantisation (such triangles are dropped).</summary>
        public bool IsDegenerate()
        {
            // cross((v1-v0),(v2-v0)) in 64-bit; components are up to 2^33 so products need decimal-free checking via double.
            double ax = (double)v1.x - v0.x, ay = (double)v1.y - v0.y, az = (double)v1.z - v0.z;
            double bx = (double)v2.x - v0.x, by = (double)v2.y - v0.y, bz = (double)v2.z - v0.z;
            double cx = ay * bz - az * by;
            double cy = az * bx - ax * bz;
            double cz = ax * by - ay * bx;
            return cx == 0.0 && cy == 0.0 && cz == 0.0;
        }
    }

    /// <summary>Legacy RTS1 material in its packed 16-byte layout.</summary>
    public struct RTPackedMaterial
    {
        public byte albedoR, albedoG, albedoB;
        public byte emitR, emitG, emitB;      // emission colour already multiplied by strength, clamped to 0..1
        public byte reflectivity;             // 0..255 -> 0..1
        public byte transparency;             // 0..255 -> 0..1
        public byte iorQ2_6;                  // index of refraction in Q2.6 (1.0 == 64)
        public byte smoothness;               // 0..255 -> 0..1

        public byte[] ToBytes()
        {
            return new byte[]
            {
                albedoR, albedoG, albedoB, 0,
                emitR, emitG, emitB, 0,
                reflectivity, transparency, iorQ2_6, smoothness,
                0, 0, 0, 0
            };
        }

        public string Key { get { return BitConverter.ToString(ToBytes()); } }
    }

    public struct RTPackedLight
    {
        public FxVec3 position;               // Q16.16
        public byte r, g, b;                  // 0..255
        public byte intensityQ4_4;            // 0..15.9375
    }

    public struct RTPackedCamera
    {
        public FxVec3 origin;                 // Q16.16 world position
        public FxVec3 dir00;                  // Q4.28 direction (unnormalised) through the centre of pixel (0,0)
        public FxVec3 dirDX;                  // Q4.28 change of direction per +1 pixel in x
        public FxVec3 dirDY;                  // Q4.28 change of direction per +1 pixel in y (downwards)
    }

    public struct RTSky
    {
        public byte horizonR, horizonG, horizonB;
        public byte zenithR, zenithG, zenithB;
    }

    /// <summary>Legacy RTS1 BVH node; this tree is built on the host before serialization.</summary>
    public struct RTBvhNode
    {
        public FxVec3 min;                    // Q16.16, conservative (built from quantised vertices)
        public FxVec3 max;
        public uint offset;                   // internal: index of left child (right = offset+1); leaf: first triangle
        public ushort count;                  // 0 = internal, otherwise number of triangles in the leaf
        public ushort reserved;

        public bool IsLeaf { get { return count != 0; } }
    }

    /// <summary>Per-object entry used only for the human-readable hierarchy report (not sent to the FPGA).</summary>
    public struct RTObjectInfo
    {
        public string path;                   // e.g. "SceneRoot/Room/LeftWall"
        public int triangleCount;
        public int materialId;
    }

    public class RTScene
    {
        public ushort width = 320;
        public ushort height = 240;
        public byte maxBounces = 3;

        public RTPackedCamera camera;
        public RTSky sky;

        public List<RTPackedLight> lights = new List<RTPackedLight>();
        public List<RTPackedMaterial> materials = new List<RTPackedMaterial>();
        public RTTriangle[] triangles = new RTTriangle[0];      // already in BVH leaf order
        public RTBvhNode[] bvh = new RTBvhNode[0];

        public List<RTObjectInfo> objects = new List<RTObjectInfo>();
        public int droppedDegenerateTriangles;

        public int BvhDepth()
        {
            if (bvh.Length == 0) return 0;
            return DepthOf(0);
        }

        private int DepthOf(int node)
        {
            if (bvh[node].IsLeaf) return 1;
            int l = DepthOf((int)bvh[node].offset);
            int r = DepthOf((int)bvh[node].offset + 1);
            return 1 + Math.Max(l, r);
        }
    }

    /// <summary>Rough prototype memory budget for the Arty A7-100T target; counts legacy RTS1 records.</summary>
    public static class RTBudget
    {
        public const int TriangleBytes = 40;
        public const int BvhNodeBytes = 32;
        public const int MaterialBytes = 16;

        // Soft limit chosen to leave BRAM for caches, stacks and frame data. Change once the RTL team fixes its memory map.
        public const int DefaultBudgetBytes = 256 * 1024;

        public static int SceneBytes(RTScene s)
        {
            return s.triangles.Length * TriangleBytes + s.bvh.Length * BvhNodeBytes + s.materials.Count * MaterialBytes;
        }
    }
}
