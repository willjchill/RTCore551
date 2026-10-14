#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RayTracing.EditorTools
{
    public static class Episode02ReferenceSetup
    {
        private const string ScenePath = "Assets/Scenes/RayTracingDemo.unity";

        [MenuItem("RayTracing/Create/Replace Current Scene With Demo")]
        public static void ReplaceCurrentSceneWithDemo()
        {
            if (GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null)
            {
                EditorUtility.DisplayDialog(
                    "Built-in Render Pipeline Required",
                    "The Episode02 reference uses Camera.OnRenderImage, which is not supported by the Scriptable Render Pipeline. Set the active Render Pipeline Asset to None, then run this command again.",
                    "OK");
                return;
            }

            Shader rayTracer = Shader.Find("Custom/RayTracer");
            Shader accumulate = Shader.Find("Hidden/Accumulate");
            if (rayTracer == null || accumulate == null)
                throw new System.InvalidOperationException("Episode02 reference shaders were not found. Check Assets/RayTracing/Reference/Episode02/Scripts/Shaders.");

            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Current Scene",
                "This replaces the currently open scene with the RayTracing demo and saves it as " + ScenePath +
                ". Any unsaved edits will be offered for saving first. An existing file at that path will be overwritten. Continue?",
                "Replace Scene",
                "Cancel");
            if (!confirmed || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateBox("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(8f, 0.5f, 8f), new Color(0.75f, 0.75f, 0.75f));
            CreateSphere("Diffuse Sphere", new Vector3(-1f, 0.7f, 0.5f), Vector3.one * 1.4f, new Color(0.85f, 0.2f, 0.15f));
            CreateSphere("Specular Sphere", new Vector3(1f, 0.55f, 1.1f), Vector3.one * 1.1f, new Color(0.85f, 0.85f, 0.9f), 0.85f);
            CreateBox("Back Wall", new Vector3(0f, 1.5f, 3f), new Vector3(8f, 3f, 0.3f), new Color(0.7f, 0.75f, 0.8f));

            var lightObject = new GameObject("Reference Sun");
            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.4f, -7f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.allowHDR = true;
            RayTracingManager manager = cameraObject.AddComponent<RayTracingManager>();
            SetManagerDefaults(manager, rayTracer, accumulate);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save RayTracing demo scene to " + ScenePath);

            AssetDatabase.Refresh();
            Selection.activeGameObject = cameraObject;
            Debug.Log("RayTracing demo replaced the active scene and was saved to " + ScenePath + ". Enter Play mode and inspect the Main Camera Game view.");
        }

        private static void CreateBox(string objectName, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objectName;
            go.transform.position = position;
            go.transform.localScale = scale;
            ConfigureModel(go, color, 0f);
        }

        private static void CreateSphere(string objectName, Vector3 position, Vector3 scale, Color color, float specularProbability = 0f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = objectName;
            go.transform.position = position;
            go.transform.localScale = scale;
            ConfigureModel(go, color, specularProbability);
        }

        private static void ConfigureModel(GameObject go, Color color, float specularProbability)
        {
            Model model = go.AddComponent<Model>();
            model.meshFilter = go.GetComponent<MeshFilter>();
            model.meshRenderer = go.GetComponent<MeshRenderer>();
            model.material.SetDefaultValues();
            model.material.colour = color;
            model.material.specularProbability = specularProbability;
            model.material.smoothness = specularProbability > 0f ? 1f : 0f;
            model.logBVHStats = true;
        }

        private static void SetManagerDefaults(RayTracingManager manager, Shader rayTracer, Shader accumulate)
        {
            var serialized = new SerializedObject(manager);
            SetObject(serialized, "rayTracingShader", rayTracer);
            SetObject(serialized, "accumulateShader", accumulate);
            SetBool(serialized, "rayTracingEnabled", true);
            SetBool(serialized, "accumulate", true);
            SetBool(serialized, "useSky", true);
            SetInt(serialized, "maxBounceCount", 2);
            SetInt(serialized, "numRaysPerPixel", 1);
            SetFloat(serialized, "defocusStrength", 0f);
            SetFloat(serialized, "divergeStrength", 0f);
            SetFloat(serialized, "focusDistance", 1f);
            SetFloat(serialized, "triTestVisScale", 50f);
            SetFloat(serialized, "boxTestVisScale", 20f);
            SetFloat(serialized, "distanceTestVisScale", 20f);
            SetInt(serialized, "visMode", 0);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(SerializedObject serialized, string name, Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new System.InvalidOperationException("Episode02 setting not found: " + name);
            property.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject serialized, string name, bool value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new System.InvalidOperationException("Episode02 setting not found: " + name);
            property.boolValue = value;
        }

        private static void SetInt(SerializedObject serialized, string name, int value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new System.InvalidOperationException("Episode02 setting not found: " + name);
            property.intValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string name, float value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new System.InvalidOperationException("Episode02 setting not found: " + name);
            property.floatValue = value;
        }
    }
}
#endif
