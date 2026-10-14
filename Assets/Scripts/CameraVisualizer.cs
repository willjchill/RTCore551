using UnityEngine;

public class CameraVisualizer : MonoBehaviour
{
    [Header("Debug Settings")]
    public float planeDistance = 1f;
    public int gridWidth = 16;
    public int gridHeight = 9;

    // OnDrawGizmos automatically runs continuously in the Editor Scene View
    void OnDrawGizmos()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        Vector3 pos = transform.position;

        // STEP 1: Visualize the FOV
        Vector3 bottomLeft  = cam.ViewportToWorldPoint(new Vector3(0, 0, planeDistance));
        Vector3 topLeft     = cam.ViewportToWorldPoint(new Vector3(0, 1, planeDistance));
        Vector3 topRight    = cam.ViewportToWorldPoint(new Vector3(1, 1, planeDistance));
        Vector3 bottomRight = cam.ViewportToWorldPoint(new Vector3(1, 0, planeDistance));

        // Draw the plane outline (Red)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);

        // Draw the frustum lines originating from the camera (Blue)
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(pos, bottomLeft);
        Gizmos.DrawLine(pos, topLeft);
        Gizmos.DrawLine(pos, topRight);
        Gizmos.DrawLine(pos, bottomRight);

        // STEP 2: Cast rays to the "pixels" (Green)
        Gizmos.color = Color.green;
        for (int y = 0; y <= gridHeight; y++)
        {
            for (int x = 0; x <= gridWidth; x++)
            {
                float u = (float)x / gridWidth;
                float v = (float)y / gridHeight;

                Vector3 pointOnPlane = cam.ViewportToWorldPoint(new Vector3(u, v, planeDistance));

                Gizmos.DrawLine(pos, pointOnPlane);
            }
        }
    }
}