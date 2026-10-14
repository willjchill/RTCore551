// RTCore551 - Task 1: deterministic reference scene for the C# <-> Python parity check.
//
// tools/rtscene_inspect.py contains a line-by-line mirror of this function. If you run
//   Unity menu  RTCoreAPI > Write Legacy RTS1 Parity Fixture      -> <project>/RTExport/reference.rtscene
//   python tools/rtscene_inspect.py check-reference RTExport/reference.rtscene
// and the script says "IDENTICAL", the C# exporter and the documented format agree byte for byte.
// All numbers are multiples of 1/16 so no float-rounding ambiguity can creep in.
using System;

namespace RTCore
{
    public static class RTSelfTest
    {
        private static uint state;

        private static uint Next()
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return state >> 16;
        }

        public static RTScene BuildReferenceScene()
        {
            state = 12345u;
            var s = new RTScene { width = 64, height = 48, maxBounces = 2 };

            s.camera = RTCameraMath.PackFromBasis(
                new double[] { 0.0, 1.0, -6.0 }, new double[] { 0.0, 0.0, 1.0 },
                new double[] { 1.0, 0.0, 0.0 }, new double[] { 0.0, 1.0, 0.0 },
                0.5, 64, 48);

            s.sky = new RTSky { horizonR = 217, horizonG = 230, horizonB = 255, zenithR = 89, zenithG = 140, zenithB = 242 };

            s.materials.Add(new RTPackedMaterial { albedoR = 204, albedoG = 204, albedoB = 204 });                                   // diffuse grey
            s.materials.Add(new RTPackedMaterial { albedoR = 242, albedoG = 242, albedoB = 242, reflectivity = 230, smoothness = 255, iorQ2_6 = 64 });   // mirror
            s.materials.Add(new RTPackedMaterial { albedoR = 255, albedoG = 255, albedoB = 255, reflectivity = 26, transparency = 230, iorQ2_6 = 96, smoothness = 255 }); // glass

            s.lights.Add(new RTPackedLight { position = FxVec3.FromDoubles(0.0, 3.75, 0.0), r = 255, g = 255, b = 255, intensityQ4_4 = 16 });
            s.lights.Add(new RTPackedLight { position = FxVec3.FromDoubles(-3.0, 3.0, -2.0), r = 255, g = 242, b = 204, intensityQ4_4 = 8 });

            var tris = new System.Collections.Generic.List<RTTriangle>();
            for (int i = 0; i < 100; i++)
            {
                double x = ((int)(Next() % 129u) - 64) / 16.0;
                double y = ((int)(Next() % 65u)) / 16.0;
                double z = ((int)(Next() % 129u) - 64) / 16.0;

                double ax = ((int)(Next() % 33u) - 16) / 16.0;
                double ay = ((int)(Next() % 33u) - 16) / 16.0;
                double az = ((int)(Next() % 33u) - 16) / 16.0;
                double bx = ((int)(Next() % 33u) - 16) / 16.0;
                double by = ((int)(Next() % 33u) - 16) / 16.0;
                double bz = ((int)(Next() % 33u) - 16) / 16.0;

                var t = new RTTriangle
                {
                    v0 = FxVec3.FromDoubles(x, y, z),
                    v1 = FxVec3.FromDoubles(x + ax, y + ay, z + az),
                    v2 = FxVec3.FromDoubles(x + bx, y + by, z + bz),
                    materialId = (ushort)(i % 3),
                    flags = RTTriangle.FlagDoubleSided
                };
                if (t.IsDegenerate()) { s.droppedDegenerateTriangles++; continue; }
                tris.Add(t);
            }

            RTTriangle[] ordered;
            RTBvhNode[] nodes;
            BVHBuilder.Build(tris.ToArray(), BVHBuilder.DefaultLeafSize, out ordered, out nodes);
            s.triangles = ordered;
            s.bvh = nodes;

            string err;
            if (!BVHBuilder.Validate(s.triangles, s.bvh, out err))
                throw new InvalidOperationException("Reference scene BVH invalid: " + err);
            return s;
        }
    }
}
