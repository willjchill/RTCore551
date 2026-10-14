// RTCore551 - Task 1: artist-facing material description that packs into the 16-byte FPGA material.
using System;
using UnityEngine;

namespace RTCore
{
    [Serializable]
    public class RTMaterialDef
    {
        [Tooltip("Diffuse colour, used exactly as shown in the inspector (no gamma conversion).")]
        public Color albedo = Color.white;

        [Tooltip("Emitted colour. Multiplied by Emission Strength and clamped to 0..1 when packed.")]
        public Color emission = Color.black;
        [Min(0f)] public float emissionStrength = 0f;

        [Range(0f, 1f)] public float reflectivity = 0f;     // 0 = diffuse only, 1 = perfect mirror
        [Range(0f, 1f)] public float transparency = 0f;     // 0 = opaque, 1 = fully refractive
        [Range(1f, 3.98f)] public float ior = 1.0f;         // index of refraction (Q2.6 on the wire, so max 3.98)
        [Range(0f, 1f)] public float smoothness = 0f;       // reserved for glossy reflection

        public RTPackedMaterial Pack()
        {
            return new RTPackedMaterial
            {
                albedoR = Fx.ToUnorm8(albedo.r),
                albedoG = Fx.ToUnorm8(albedo.g),
                albedoB = Fx.ToUnorm8(albedo.b),
                emitR = Fx.ToUnorm8(emission.r * emissionStrength),
                emitG = Fx.ToUnorm8(emission.g * emissionStrength),
                emitB = Fx.ToUnorm8(emission.b * emissionStrength),
                reflectivity = Fx.ToUnorm8(reflectivity),
                transparency = Fx.ToUnorm8(transparency),
                iorQ2_6 = (byte)Mathf.Clamp(Mathf.RoundToInt(ior * 64f), 64, 255),
                smoothness = Fx.ToUnorm8(smoothness)
            };
        }

        public static RTMaterialDef Diffuse(Color c)
        {
            return new RTMaterialDef { albedo = c };
        }

        public static RTMaterialDef Mirror(Color tint, float reflectivity = 0.9f)
        {
            return new RTMaterialDef { albedo = tint, reflectivity = reflectivity, smoothness = 1f };
        }

        public static RTMaterialDef Glass(float ior = 1.5f, float transparency = 0.9f)
        {
            return new RTMaterialDef { albedo = Color.white, transparency = transparency, ior = ior, reflectivity = 0.1f, smoothness = 1f };
        }
    }
}
