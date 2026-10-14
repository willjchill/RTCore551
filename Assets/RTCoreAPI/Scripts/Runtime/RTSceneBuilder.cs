// Legacy RTS1 exporter: flattens the Unity hierarchy into world-space triangles and builds a host-side BVH.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTCore
{
    public static class RTSceneBuilder
    {
        public static RTScene Build(Transform root, Camera cam, int width, int height, int maxBounces,
                                    int bvhLeafSize, Color skyHorizon, Color skyZenith)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (cam == null) throw new ArgumentNullException("cam");
            if (width < 1 || width > ushort.MaxValue || height < 1 || height > ushort.MaxValue)
                throw new ArgumentException("Resolution must be 1..65535 in each direction.");

            var scene = new RTScene
            {
                width = (ushort)width,
                height = (ushort)height,
                maxBounces = (byte)Mathf.Clamp(maxBounces, 0, 255),
                camera = RTCameraRig.Pack(cam, width, height),
                sky = new RTSky
                {
                    horizonR = Fx.ToUnorm8(skyHorizon.r), horizonG = Fx.ToUnorm8(skyHorizon.g), horizonB = Fx.ToUnorm8(skyHorizon.b),
                    zenithR = Fx.ToUnorm8(skyZenith.r), zenithG = Fx.ToUnorm8(skyZenith.g), zenithB = Fx.ToUnorm8(skyZenith.b)
                }
            };

            // ---- objects -> triangles (hierarchy order, active objects only, so the result is deterministic)
            var materialIds = new Dictionary<string, int>();
            var tris = new List<RTTriangle>();

            foreach (RTSceneObject obj in root.GetComponentsInChildren<RTSceneObject>(false))
            {
                Mesh mesh = obj.Mesh;
                string path = PathOf(obj.transform, root);
                if (mesh == null || !mesh.isReadable)
                {
                    Debug.LogWarning("RTCore: skipping '" + path + "' (no readable mesh; enable Read/Write on imported models).");
                    continue;
                }

                RTPackedMaterial packed = obj.material.Pack();
                int matId;
                if (!materialIds.TryGetValue(packed.Key, out matId))
                {
                    matId = scene.materials.Count;
                    materialIds.Add(packed.Key, matId);
                    scene.materials.Add(packed);
                }

                Vector3[] verts = mesh.vertices;
                int[] indices = mesh.triangles;
                Matrix4x4 toWorld = obj.transform.localToWorldMatrix;
                int added = 0;

                try
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var t = new RTTriangle
                        {
                            v0 = ToFx(toWorld.MultiplyPoint3x4(verts[indices[i]])),
                            v1 = ToFx(toWorld.MultiplyPoint3x4(verts[indices[i + 1]])),
                            v2 = ToFx(toWorld.MultiplyPoint3x4(verts[indices[i + 2]])),
                            materialId = (ushort)matId,
                            flags = RTTriangle.FlagDoubleSided
                        };
                        if (t.IsDegenerate()) { scene.droppedDegenerateTriangles++; continue; }
                        tris.Add(t);
                        added++;
                    }
                }
                catch (OverflowException e)
                {
                    throw new OverflowException("RTCore: object '" + path + "' is out of range. " + e.Message);
                }

                scene.objects.Add(new RTObjectInfo { path = path, triangleCount = added, materialId = matId });
            }

            // ---- lights
            foreach (RTLightSource l in root.GetComponentsInChildren<RTLightSource>(false))
                scene.lights.Add(l.Pack());
            if (scene.lights.Count == 0)
                Debug.LogWarning("RTCore: scene has no RTLightSource under the root; shading will have no direct light.");

            // ---- BVH
            RTTriangle[] ordered;
            RTBvhNode[] nodes;
            BVHBuilder.Build(tris.ToArray(), bvhLeafSize, out ordered, out nodes);
            scene.triangles = ordered;
            scene.bvh = nodes;

            string error;
            if (!BVHBuilder.Validate(scene.triangles, scene.bvh, out error))
                throw new InvalidOperationException("RTCore: BVH validation failed: " + error);

            int bytes = RTBudget.SceneBytes(scene);
            if (bytes > RTBudget.DefaultBudgetBytes)
                Debug.LogWarning("RTCore: scene needs " + (bytes / 1024) + " KiB, above the soft budget of " +
                                 (RTBudget.DefaultBudgetBytes / 1024) + " KiB. Reduce triangle count or raise RTBudget.DefaultBudgetBytes.");

            return scene;
        }

        private static FxVec3 ToFx(Vector3 p)
        {
            return FxVec3.FromDoubles(p.x, p.y, p.z);
        }

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root.parent; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
