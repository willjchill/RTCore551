// RTCore551 - Task 1: marks a GameObject (with a MeshFilter) as part of the ray traced scene.
// Object hierarchy:  SceneRoot (RTSceneController)  ->  GameObjects with RTSceneObject  ->  Mesh  ->  triangles.
// Every triangle of the mesh gets this object's single material. Nested children are picked up automatically.
using UnityEngine;

namespace RTCore
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class RTSceneObject : MonoBehaviour
    {
        public RTMaterialDef material = new RTMaterialDef();

        public Mesh Mesh
        {
            get { var mf = GetComponent<MeshFilter>(); return mf != null ? mf.sharedMesh : null; }
        }
    }
}
