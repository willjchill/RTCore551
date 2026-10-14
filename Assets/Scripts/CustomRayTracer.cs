using UnityEngine;
using System.Collections.Generic;

public class CustomRaytracer : MonoBehaviour
{
    [Header("Render Settings")]
    public int renderWidth = 128; // Keep low for CPU rendering (e.g., 64-256)
    public int renderHeight = 128;
    public float planeDistance = 1f;

    private Camera cam;
    private Texture2D renderTarget;
    private List<Triangle> sceneTriangles = new List<Triangle>();

    // Struct to hold world-space triangle data
    struct Triangle
    {
        public Vector3 v0, v1, v2;
    }

    void Start()
    {
        cam = GetComponent<Camera>();
        renderTarget = new Texture2D(renderWidth, renderHeight);
        renderTarget.filterMode = FilterMode.Point; // Crisp pixels
        
        CacheSceneGeometry();
    }

    void Update()
    {
        RenderFrame();
    }

    void CacheSceneGeometry()
    {
        // Extract all mesh data in the scene and convert to world space
        MeshFilter[] meshFilters = FindObjectsByType<MeshFilter>();
        foreach (var mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Transform t = mf.transform;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Triangle tri;
                // Transform local vertices to world space coordinates
                tri.v0 = t.TransformPoint(vertices[triangles[i]]);
                tri.v1 = t.TransformPoint(vertices[triangles[i + 1]]);
                tri.v2 = t.TransformPoint(vertices[triangles[i + 2]]);
                sceneTriangles.Add(tri);
            }
        }
    }

    void RenderFrame()
    {
        Vector3 camPos = transform.position;

        for (int y = 0; y < renderHeight; y++)
        {
            for (int x = 0; x < renderWidth; x++)
            {
                // 1. Ray Generation
                float u = (float)x / renderWidth;
                float v = (float)y / renderHeight;
                Vector3 pixelWorldPos = cam.ViewportToWorldPoint(new Vector3(u, v, planeDistance));
                Vector3 rayDir = (pixelWorldPos - camPos).normalized;

                // 2. Intersection Testing
                bool hit = false;
                foreach (var tri in sceneTriangles)
                {
                    if (IntersectTriangle(camPos, rayDir, tri.v0, tri.v1, tri.v2, out float t))
                    {
                        hit = true;
                        break; // Stop checking once we hit anything (no depth sorting yet)
                    }
                }

                // 3. Shading
                Color pixelColor = hit ? Color.white : Color.black;
                renderTarget.SetPixel(x, y, pixelColor);
            }
        }
        renderTarget.Apply();
    }

    // Möller–Trumbore intersection algorithm
    bool IntersectTriangle(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0;
        const float EPSILON = 0.0000001f;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 h = Vector3.Cross(rayDir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -EPSILON && a < EPSILON)
            return false; // Ray is parallel to triangle

        float f = 1.0f / a;
        Vector3 s = rayOrigin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(rayDir, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = f * Vector3.Dot(edge2, q);
        return t > EPSILON; // Ray intersection
    }

    // Draw the texture to the screen in the Game view
    void OnGUI()
    {
        if (renderTarget != null)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), renderTarget, ScaleMode.StretchToFill);
        }
    }
}