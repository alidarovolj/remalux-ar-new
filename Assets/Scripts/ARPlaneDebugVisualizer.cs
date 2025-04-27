using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneDebugVisualizer : MonoBehaviour
{
    [SerializeField]
    private Color horizontalLineColor = Color.blue;
    
    [SerializeField]
    private Color verticalLineColor = Color.red;
    
    [SerializeField]
    private float lineWidth = 0.03f;
    
    [SerializeField]
    private bool showCenterPoints = true;
    
    [SerializeField]
    private float centerPointSize = 0.1f;
    
    [SerializeField]
    private bool showArrows = true;
    
    [SerializeField]
    private float arrowSize = 0.2f;
    
    private ARPlaneManager planeManager;
    private Dictionary<TrackableId, List<Vector3>> planeVertices = new Dictionary<TrackableId, List<Vector3>>();
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
    }
    
    private void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
    }
    
    private void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Store vertices for each added or updated plane
        foreach (ARPlane plane in args.added)
        {
            UpdatePlaneVertices(plane);
        }
        
        foreach (ARPlane plane in args.updated)
        {
            UpdatePlaneVertices(plane);
        }
        
        // Remove data for removed planes
        foreach (ARPlane plane in args.removed)
        {
            if (planeVertices.ContainsKey(plane.trackableId))
            {
                planeVertices.Remove(plane.trackableId);
            }
        }
    }
    
    private void UpdatePlaneVertices(ARPlane plane)
    {
        // Get the mesh vertices
        Mesh mesh = plane.GetComponent<MeshFilter>()?.mesh;
        if (mesh == null) return;
        
        Vector3[] vertices = mesh.vertices;
        
        // Transform vertices to world space
        List<Vector3> worldVertices = new List<Vector3>();
        foreach (Vector3 vertex in vertices)
        {
            worldVertices.Add(plane.transform.TransformPoint(vertex));
        }
        
        // Store the vertices
        planeVertices[plane.trackableId] = worldVertices;
    }
    
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || planeManager == null) return;
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (!planeVertices.TryGetValue(plane.trackableId, out List<Vector3> vertices) || vertices.Count == 0)
                continue;
            
            // Set color based on plane alignment
            Gizmos.color = plane.alignment == PlaneAlignment.Vertical ? verticalLineColor : horizontalLineColor;
            
            // Draw boundary lines
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 current = vertices[i];
                Vector3 next = vertices[(i + 1) % vertices.Count];
                
                // Draw a thick line using multiple lines
                for (float t = -lineWidth / 2; t <= lineWidth / 2; t += lineWidth / 3)
                {
                    // Offset the line in a direction perpendicular to the plane's normal and the line
                    Vector3 dir = Vector3.Cross(plane.normal, (next - current).normalized).normalized;
                    Gizmos.DrawLine(current + dir * t, next + dir * t);
                }
            }
            
            if (showCenterPoints)
            {
                // Draw center point
                Vector3 center = plane.center;
                Gizmos.DrawSphere(center, centerPointSize);
            }
            
            if (showArrows)
            {
                // Draw normal direction arrow
                Vector3 center = plane.center;
                Gizmos.DrawLine(center, center + plane.normal * arrowSize);
                
                // Draw arrow head
                Vector3 arrowTip = center + plane.normal * arrowSize;
                Vector3 right = Vector3.Cross(plane.normal, Vector3.up).normalized;
                if (right.magnitude < 0.1f)
                    right = Vector3.Cross(plane.normal, Vector3.right).normalized;
                    
                Vector3 up = Vector3.Cross(right, plane.normal).normalized;
                
                Gizmos.DrawLine(arrowTip, arrowTip - plane.normal * (arrowSize * 0.3f) + right * (arrowSize * 0.3f));
                Gizmos.DrawLine(arrowTip, arrowTip - plane.normal * (arrowSize * 0.3f) - right * (arrowSize * 0.3f));
                Gizmos.DrawLine(arrowTip, arrowTip - plane.normal * (arrowSize * 0.3f) + up * (arrowSize * 0.3f));
                Gizmos.DrawLine(arrowTip, arrowTip - plane.normal * (arrowSize * 0.3f) - up * (arrowSize * 0.3f));
            }
        }
    }
} 