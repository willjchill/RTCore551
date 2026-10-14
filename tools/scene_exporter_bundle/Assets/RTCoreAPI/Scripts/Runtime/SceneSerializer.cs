// RTCore551 - Task 1: turns an RTScene into the byte layout the FPGA team reads.
// Pure C# (System.IO only). The format is documented in README.md and mirrored by tools/rtscene_inspect.py.
//
// File layout, everything little-endian ("RTS1", version 1):
//   Header     32 bytes
//   Camera     48 bytes   origin(3xi32 Q16.16) dir00(3xi32 Q4.28) dirDX(3xi32 Q4.28) dirDY(3xi32 Q4.28)
//   Sky         8 bytes   horizon rgb,0  zenith rgb,0
//   Lights     16 bytes x numLights
//   Materials  16 bytes x numMaterials
//   Triangles  40 bytes x numTriangles   (already in BVH leaf order)
//   BVH nodes  32 bytes x numBvhNodes
//   CRC32       4 bytes   IEEE CRC-32 over every byte before it (same as zlib.crc32)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RTCore
{
    public static class SceneSerializer
    {
        public const string Magic = "RTS1";
        public const ushort Version = 1;
        public const int HeaderBytes = 32;
        public const int CameraBytes = 48;
        public const int SkyBytes = 8;
        public const int LightBytes = 16;
        public const int MaterialBytes = 16;
        public const int TriangleBytes = 40;
        public const int BvhNodeBytes = 32;

        // ------------------------------------------------------------------ section packers

        public static byte[] PackCamera(RTPackedCamera c)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            WriteVec(w, c.origin);
            WriteVec(w, c.dir00);
            WriteVec(w, c.dirDX);
            WriteVec(w, c.dirDY);
            return ms.ToArray();
        }

        public static byte[] PackSky(RTSky s)
        {
            return new byte[] { s.horizonR, s.horizonG, s.horizonB, 0, s.zenithR, s.zenithG, s.zenithB, 0 };
        }

        public static byte[] PackLights(List<RTPackedLight> lights)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            foreach (RTPackedLight l in lights)
            {
                WriteVec(w, l.position);
                w.Write(l.r); w.Write(l.g); w.Write(l.b); w.Write(l.intensityQ4_4);
            }
            return ms.ToArray();
        }

        public static byte[] PackMaterials(List<RTPackedMaterial> mats)
        {
            var ms = new MemoryStream();
            foreach (RTPackedMaterial m in mats) { byte[] b = m.ToBytes(); ms.Write(b, 0, b.Length); }
            return ms.ToArray();
        }

        public static byte[] PackTriangles(RTTriangle[] tris)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            foreach (RTTriangle t in tris)
            {
                WriteVec(w, t.v0); WriteVec(w, t.v1); WriteVec(w, t.v2);
                w.Write(t.materialId);
                w.Write(t.flags);
            }
            return ms.ToArray();
        }

        public static byte[] PackBvh(RTBvhNode[] nodes)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            foreach (RTBvhNode n in nodes)
            {
                WriteVec(w, n.min);
                WriteVec(w, n.max);
                w.Write(n.offset);
                w.Write(n.count);
                w.Write(n.reserved);
            }
            return ms.ToArray();
        }

        private static void WriteVec(BinaryWriter w, FxVec3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }

        // ------------------------------------------------------------------ whole file

        public static byte[] Serialize(RTScene s)
        {
            if (s.materials.Count > ushort.MaxValue) throw new InvalidOperationException("Too many materials (max 65535).");
            if (s.lights.Count > ushort.MaxValue) throw new InvalidOperationException("Too many lights (max 65535).");
            foreach (RTTriangle t in s.triangles)
                if (t.materialId >= s.materials.Count) throw new InvalidOperationException("Triangle references missing material " + t.materialId);

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);

            w.Write(Encoding.ASCII.GetBytes(Magic));            // 0
            w.Write(Version);                                   // 4
            w.Write((ushort)HeaderBytes);                       // 6
            w.Write(s.width);                                   // 8
            w.Write(s.height);                                  // 10
            w.Write((byte)Fx.PosFracBits);                      // 12
            w.Write((byte)Fx.CamFracBits);                      // 13
            w.Write(s.maxBounces);                              // 14
            w.Write((byte)0);                                   // 15 reserved
            w.Write((uint)s.triangles.Length);                  // 16
            w.Write((ushort)s.materials.Count);                 // 20
            w.Write((ushort)s.lights.Count);                    // 22
            w.Write((uint)s.bvh.Length);                        // 24
            w.Write((uint)0);                                   // 28 flags, reserved

            Append(ms, PackCamera(s.camera));
            Append(ms, PackSky(s.sky));
            Append(ms, PackLights(s.lights));
            Append(ms, PackMaterials(s.materials));
            Append(ms, PackTriangles(s.triangles));
            Append(ms, PackBvh(s.bvh));

            byte[] body = ms.ToArray();
            uint crc = Crc32.Compute(body);
            var outMs = new MemoryStream();
            outMs.Write(body, 0, body.Length);
            new BinaryWriter(outMs).Write(crc);
            return outMs.ToArray();
        }

        private static void Append(MemoryStream ms, byte[] b) { ms.Write(b, 0, b.Length); }

        // ------------------------------------------------------------------ files for the team

        /// <summary>
        /// Writes into outDir:
        ///   {name}.rtscene   binary scene (for the UART sender and the Python inspector)
        ///   {name}_report.txt hierarchy + stats
        ///   mem/*.mem         one 32-bit hex word per line, for $readmemh in RTL testbenches
        /// </summary>
        public static void WriteAll(RTScene s, string outDir, string name)
        {
            Directory.CreateDirectory(outDir);
            string memDir = Path.Combine(outDir, "mem");
            Directory.CreateDirectory(memDir);

            File.WriteAllBytes(Path.Combine(outDir, name + ".rtscene"), Serialize(s));
            File.WriteAllText(Path.Combine(outDir, name + "_report.txt"), Describe(s));

            WriteMem(Path.Combine(memDir, "camera.mem"), PackCamera(s.camera));
            WriteMem(Path.Combine(memDir, "lights.mem"), PackLights(s.lights));
            WriteMem(Path.Combine(memDir, "materials.mem"), PackMaterials(s.materials));
            WriteMem(Path.Combine(memDir, "triangles.mem"), PackTriangles(s.triangles));
            WriteMem(Path.Combine(memDir, "bvh.mem"), PackBvh(s.bvh));
        }

        /// <summary>One little-endian 32-bit word per line, lowercase hex, no prefix. All records are a multiple of 4 bytes.</summary>
        public static void WriteMem(string path, byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                uint word = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
                sb.Append(word.ToString("x8")).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        public static string Describe(RTScene s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RTCore551 scene report");
            sb.AppendLine("======================");
            sb.AppendLine("Resolution      : " + s.width + " x " + s.height + "  (" + (s.width * s.height) + " primary rays)");
            sb.AppendLine("Max bounces     : " + s.maxBounces);
            sb.AppendLine("Triangles       : " + s.triangles.Length + "  (dropped degenerate: " + s.droppedDegenerateTriangles + ")");
            sb.AppendLine("Materials       : " + s.materials.Count);
            sb.AppendLine("Lights          : " + s.lights.Count);
            sb.AppendLine("BVH nodes       : " + s.bvh.Length + "  depth " + s.BvhDepth());
            int bytes = RTBudget.SceneBytes(s);
            sb.AppendLine("Scene memory    : " + bytes + " bytes (" + (bytes / 1024.0).ToString("0.0") + " KiB), soft budget " + (RTBudget.DefaultBudgetBytes / 1024) + " KiB");
            sb.AppendLine("Camera origin   : " + s.camera.origin);
            sb.AppendLine();
            sb.AppendLine("Object hierarchy (flattened into the triangle list in this order of discovery)");
            sb.AppendLine("path | triangles | materialId");
            foreach (RTObjectInfo o in s.objects)
                sb.AppendLine(o.path + " | " + o.triangleCount + " | " + o.materialId);
            sb.AppendLine();
            sb.AppendLine("Materials (packed values)");
            for (int i = 0; i < s.materials.Count; i++)
            {
                RTPackedMaterial m = s.materials[i];
                sb.AppendLine("  [" + i + "] albedo=(" + m.albedoR + "," + m.albedoG + "," + m.albedoB + ")" +
                              " emit=(" + m.emitR + "," + m.emitG + "," + m.emitB + ")" +
                              " refl=" + m.reflectivity + " transp=" + m.transparency +
                              " ior=" + (m.iorQ2_6 / 64.0).ToString("0.000") + " smooth=" + m.smoothness);
            }
            return sb.ToString();
        }
    }

    /// <summary>IEEE 802.3 CRC-32 (reflected, poly 0xEDB88320). Identical to Python's zlib.crc32.</summary>
    public static class Crc32
    {
        private static readonly uint[] table = MakeTable();

        private static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
