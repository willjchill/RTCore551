// RTCore551 - Task 1: Unity-free camera packing, shared by RTCameraRig and the parity self-test.
using System;

namespace RTCore
{
    public static class RTCameraMath
    {
        /// <summary>
        /// origin / fwd / right / up are 3-element arrays in world space (fwd, right, up orthonormal).
        /// halfH = tan(verticalFov / 2). The image plane sits 1 unit in front of the origin.
        /// </summary>
        public static RTPackedCamera PackFromBasis(double[] origin, double[] fwd, double[] right, double[] up,
                                                   double halfH, int width, int height)
        {
            double halfW = halfH * width / height;
            double du = 2.0 * halfW / width;
            double dv = 2.0 * halfH / height;
            double sx = -halfW + du * 0.5;      // pixel (0,0) centre, in "right" units
            double sy = halfH - dv * 0.5;       // pixel (0,0) centre, in "up" units

            var d00 = new double[3];
            var dx = new double[3];
            var dy = new double[3];
            for (int k = 0; k < 3; k++)
            {
                d00[k] = fwd[k] + sx * right[k] + sy * up[k];
                dx[k] = du * right[k];
                dy[k] = -dv * up[k];
            }

            return new RTPackedCamera
            {
                origin = FxVec3.FromDoubles(origin[0], origin[1], origin[2]),
                dir00 = FxVec3.FromDoubles(d00[0], d00[1], d00[2], Fx.CamFracBits),
                dirDX = FxVec3.FromDoubles(dx[0], dx[1], dx[2], Fx.CamFracBits),
                dirDY = FxVec3.FromDoubles(dy[0], dy[1], dy[2], Fx.CamFracBits)
            };
        }
    }
}
