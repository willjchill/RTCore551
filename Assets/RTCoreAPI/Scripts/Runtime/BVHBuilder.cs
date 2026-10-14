// RTCore551 - Task 1: BVH construction in pure integer space (no UnityEngine dependency).
//
// Why integer space: the AABBs are computed from the *already quantised* Q16.16 vertices, so a node box
// always contains its triangles exactly. If we built in float and quantised afterwards, a vertex could end up
// half an LSB outside its box and the RTL would occasionally miss a triangle.
//
// Layout rules the RTL can rely on:
//   * nodes[0] is the root.
//   * Internal node: count == 0, children are nodes[offset] and nodes[offset + 1] (always adjacent).
//   * Leaf node:     count  > 0, triangles are triangles[offset .. offset + count - 1].
//   * The returned triangle array is already reordered so leaves are contiguous.
using System;
using System.Collections.Generic;

namespace RTCore
{
    public static class BVHBuilder
    {
        public const int DefaultLeafSize = 4;

        public static void Build(RTTriangle[] input, int leafSize, out RTTriangle[] ordered, out RTBvhNode[] nodes)
        {
            if (leafSize < 1) leafSize = 1;
            if (leafSize > 255) leafSize = 255;

            if (input == null || input.Length == 0)
            {
                ordered = new RTTriangle[0];
                nodes = new RTBvhNode[0];
                return;
            }

            var b = new Builder(input, leafSize);
            b.Run();
            ordered = new RTTriangle[input.Length];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = input[b.idx[i]];
            nodes = b.nodes.ToArray();
        }

        /// <summary>Checks the structural invariants listed at the top of this file. Returns true when the tree is valid.</summary>
        public static bool Validate(RTTriangle[] tris, RTBvhNode[] nodes, out string error)
        {
            error = null;
            if (tris.Length == 0) return nodes.Length == 0 || Fail(out error, "non-empty BVH for empty scene");
            if (nodes.Length == 0) return Fail(out error, "empty BVH for non-empty scene");

            var covered = new int[tris.Length];
            var visited = new bool[nodes.Length];
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                if (n < 0 || n >= nodes.Length) return Fail(out error, "node index out of range: " + n);
                if (visited[n]) return Fail(out error, "node visited twice (cycle or shared child): " + n);
                visited[n] = true;
                RTBvhNode node = nodes[n];
                if (node.IsLeaf)
                {
                    long end = (long)node.offset + node.count;
                    if (end > tris.Length) return Fail(out error, "leaf " + n + " reads past the triangle array");
                    for (int t = (int)node.offset; t < end; t++)
                    {
                        covered[t]++;
                        if (!Contains(node, tris[t].BoundsMin, tris[t].BoundsMax))
                            return Fail(out error, "leaf " + n + " box does not contain triangle " + t);
                    }
                }
                else
                {
                    long l = node.offset, r = node.offset + 1L;
                    if (r >= nodes.Length) return Fail(out error, "internal node " + n + " has children out of range");
                    if (!Contains(node, nodes[l].min, nodes[l].max) || !Contains(node, nodes[r].min, nodes[r].max))
                        return Fail(out error, "internal node " + n + " box does not contain its children");
                    stack.Push((int)r);
                    stack.Push((int)l);
                }
            }
            for (int i = 0; i < covered.Length; i++)
                if (covered[i] != 1) return Fail(out error, "triangle " + i + " referenced " + covered[i] + " times (expected 1)");
            return true;
        }

        private static bool Contains(RTBvhNode outer, FxVec3 min, FxVec3 max)
        {
            return outer.min.x <= min.x && outer.min.y <= min.y && outer.min.z <= min.z &&
                   outer.max.x >= max.x && outer.max.y >= max.y && outer.max.z >= max.z;
        }

        private static bool Fail(out string error, string msg) { error = msg; return false; }

        // ------------------------------------------------------------------------------------------------

        private class Builder
        {
            public readonly int[] idx;
            public readonly List<RTBvhNode> nodes = new List<RTBvhNode>();

            private readonly RTTriangle[] tris;
            private readonly FxVec3[] triMin;
            private readonly FxVec3[] triMax;
            private readonly long[][] centroid3;     // [axis][triangle] = v0+v1+v2
            private readonly int leafSize;

            public Builder(RTTriangle[] input, int leafSize)
            {
                tris = input;
                this.leafSize = leafSize;
                int n = input.Length;
                idx = new int[n];
                triMin = new FxVec3[n];
                triMax = new FxVec3[n];
                centroid3 = new long[3][] { new long[n], new long[n], new long[n] };
                for (int i = 0; i < n; i++)
                {
                    idx[i] = i;
                    triMin[i] = input[i].BoundsMin;
                    triMax[i] = input[i].BoundsMax;
                    for (int a = 0; a < 3; a++) centroid3[a][i] = input[i].CentroidTimes3(a);
                }
            }

            public void Run()
            {
                nodes.Add(new RTBvhNode());
                BuildNode(0, 0, tris.Length);
            }

            private void BuildNode(int nodeIndex, int start, int count)
            {
                FxVec3 bmin = triMin[idx[start]], bmax = triMax[idx[start]];
                long[] cmin = new long[3], cmax = new long[3];
                for (int a = 0; a < 3; a++) { cmin[a] = centroid3[a][idx[start]]; cmax[a] = cmin[a]; }

                for (int i = start + 1; i < start + count; i++)
                {
                    int t = idx[i];
                    bmin = FxVec3.Min(bmin, triMin[t]);
                    bmax = FxVec3.Max(bmax, triMax[t]);
                    for (int a = 0; a < 3; a++)
                    {
                        long c = centroid3[a][t];
                        if (c < cmin[a]) cmin[a] = c;
                        if (c > cmax[a]) cmax[a] = c;
                    }
                }

                var node = new RTBvhNode { min = bmin, max = bmax };

                if (count <= leafSize)
                {
                    node.offset = (uint)start;
                    node.count = (ushort)count;
                    nodes[nodeIndex] = node;
                    return;
                }

                // split on the axis where the centroids spread the most, at the median (balanced tree)
                int axis = 0;
                long best = cmax[0] - cmin[0];
                for (int a = 1; a < 3; a++)
                {
                    long ext = cmax[a] - cmin[a];
                    if (ext > best) { best = ext; axis = a; }
                }

                if (best > 0)
                {
                    long[] key = centroid3[axis];
                    Array.Sort(idx, start, count, new CentroidComparer(key));
                }
                // best == 0: every centroid coincides, any split is as good as another, keep the order as is.

                int mid = start + count / 2;
                int left = nodes.Count;
                nodes.Add(new RTBvhNode());
                nodes.Add(new RTBvhNode());

                node.offset = (uint)left;
                node.count = 0;
                nodes[nodeIndex] = node;

                BuildNode(left, start, mid - start);
                BuildNode(left + 1, mid, start + count - mid);
            }
        }

        private class CentroidComparer : IComparer<int>
        {
            private readonly long[] key;
            public CentroidComparer(long[] key) { this.key = key; }
            public int Compare(int a, int b)
            {
                int c = key[a].CompareTo(key[b]);
                return c != 0 ? c : a.CompareTo(b);      // tie-break on index => fully deterministic output
            }
        }
    }
}
