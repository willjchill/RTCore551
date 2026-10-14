#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RTCore.EditorTools
{
    public static class RTS1ParityMenu
    {
        [MenuItem("RTCoreAPI/Write Legacy RTS1 Parity Fixture")]
        public static void WriteReferenceScene()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "RTExport"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "reference.rtscene");
            File.WriteAllBytes(file, SceneSerializer.Serialize(RTSelfTest.BuildReferenceScene()));
            Debug.Log("RTCoreAPI: wrote " + file + ". Run the Python checker with check-reference to verify byte parity.");
        }
    }
}
#endif
