// RTCore551 - Task 1: converts a Unity Camera into the three vectors the RTL ray generator needs.
//
//   ray origin    = camera.origin                                  (Q16.16, same for every pixel)
//   ray direction = dir00 + x * dirDX + y * dirDY                  (Q4.28, NOT normalised, x,y = pixel column,row)
//
// Pixel (0,0) is the top-left pixel and its centre is used. The image plane sits 1 unit in front of the camera,
// so the forward component of every direction has length exactly 1. Unity is left-handed, Y up; right = +X of the
// camera, row index grows downwards.
using System;
using UnityEngine;

namespace RTCore
{
    public static class RTCameraRig
    {
        public static RTPackedCamera Pack(Camera cam, int width, int height)
        {
            if (cam.orthographic) throw new InvalidOperationException("RTCore needs a perspective camera.");

            Transform t = cam.transform;
            double halfH = Math.Tan(cam.fieldOfView * 0.5 * Math.PI / 180.0);   // fieldOfView is the vertical FOV
            return RTCameraMath.PackFromBasis(Arr(t.position), Arr(t.forward), Arr(t.right), Arr(t.up), halfH, width, height);
        }

        private static double[] Arr(Vector3 v) { return new double[] { v.x, v.y, v.z }; }
    }
}
