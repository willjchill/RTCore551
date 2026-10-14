// RTCore551 - Task 1: point light for the ray traced scene (position = transform.position).
using UnityEngine;

namespace RTCore
{
    [DisallowMultipleComponent]
    public class RTLightSource : MonoBehaviour
    {
        public Color color = Color.white;
        [Range(0f, 15.9f)] public float intensity = 1f;     // Q4.4 on the wire: 0 .. 15.9375

        public RTPackedLight Pack()
        {
            Vector3 p = transform.position;
            return new RTPackedLight
            {
                position = FxVec3.FromDoubles(p.x, p.y, p.z),
                r = Fx.ToUnorm8(color.r),
                g = Fx.ToUnorm8(color.g),
                b = Fx.ToUnorm8(color.b),
                intensityQ4_4 = (byte)Mathf.Clamp(Mathf.RoundToInt(intensity * 16f), 0, 255)
            };
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
        }
    }
}
