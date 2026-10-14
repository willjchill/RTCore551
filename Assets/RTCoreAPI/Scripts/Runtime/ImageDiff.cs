// RTCore551 - Task 1: pixel-wise comparison used for the golden-reference check
// (reference image  minus  FPGA image  ==  black  ->  no error).
using System;
using System.IO;
using UnityEngine;

namespace RTCore
{
    public struct ImageDiffResult
    {
        public int width, height;
        public int mismatchedPixels;      // pixels where any channel differs by more than the tolerance
        public int maxChannelDiff;        // 0..255
        public double meanAbsDiff;        // average |difference| per channel, 0..255

        public bool IsIdentical { get { return mismatchedPixels == 0; } }

        public override string ToString()
        {
            return width + "x" + height + ": mismatched=" + mismatchedPixels + " (" +
                   (100.0 * mismatchedPixels / Math.Max(1, width * height)).ToString("0.00") + "%), max diff=" + maxChannelDiff +
                   ", mean abs diff=" + meanAbsDiff.ToString("0.000");
        }
    }

    public static class ImageDiff
    {
        public static ImageDiffResult Compare(Texture2D a, Texture2D b, int tolerance, out Texture2D diffImage)
        {
            if (a.width != b.width || a.height != b.height)
                throw new ArgumentException("Image sizes differ: " + a.width + "x" + a.height + " vs " + b.width + "x" + b.height);

            Color32[] pa = a.GetPixels32();
            Color32[] pb = b.GetPixels32();
            var diff = new Color32[pa.Length];
            var res = new ImageDiffResult { width = a.width, height = a.height };
            long sum = 0;

            for (int i = 0; i < pa.Length; i++)
            {
                int dr = Math.Abs(pa[i].r - pb[i].r);
                int dg = Math.Abs(pa[i].g - pb[i].g);
                int db = Math.Abs(pa[i].b - pb[i].b);
                int m = Math.Max(dr, Math.Max(dg, db));
                sum += dr + dg + db;
                if (m > res.maxChannelDiff) res.maxChannelDiff = m;
                if (m > tolerance) res.mismatchedPixels++;
                diff[i] = new Color32((byte)dr, (byte)dg, (byte)db, 255);   // black everywhere the images agree
            }

            res.meanAbsDiff = sum / (3.0 * pa.Length);
            diffImage = new Texture2D(a.width, a.height, TextureFormat.RGB24, false);
            diffImage.SetPixels32(diff);
            diffImage.Apply();
            return res;
        }

        /// <summary>Loads two PNG files, compares them and writes the difference image next to the first file.</summary>
        public static ImageDiffResult CompareFiles(string referencePng, string fpgaPng, int tolerance, string diffPngOut)
        {
            var a = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var b = new Texture2D(2, 2, TextureFormat.RGB24, false);
            a.LoadImage(File.ReadAllBytes(referencePng));
            b.LoadImage(File.ReadAllBytes(fpgaPng));
            Texture2D diff;
            ImageDiffResult r = Compare(a, b, tolerance, out diff);
            if (!string.IsNullOrEmpty(diffPngOut)) File.WriteAllBytes(diffPngOut, diff.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
            UnityEngine.Object.DestroyImmediate(diff);
            return r;
        }
    }
}
