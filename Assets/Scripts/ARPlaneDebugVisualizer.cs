using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneDebugVisualizer : MonoBehaviour
{
    [SerializeField]
    private bool drawBoundaries = true;
    
    [SerializeField]
    private Color verticalPlaneColor = Color.magenta;
    
    [SerializeField]
    private Color horizontalPlaneColor = Color.cyan;
    
    [SerializeField]
    private float outlineWidth = 0.05f;
    
    [SerializeField]
    private bool draw3DIndicators = true;
    
    [SerializeField]
    private GameObject indicatorPrefab;
    
    private ARPlaneManager planeManager;
    private Dictionary<TrackableId, List<GameObject>> planeIndicators = new Dictionary<TrackableId, List<GameObject>>();
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        
        // Create indicator prefab if not assigned
        if (indicatorPrefab == null && draw3DIndicators)
        {
            CreateDefaultIndicatorPrefab();
        }
    }
    
    private void CreateDefaultIndicatorPrefab()
    {
        indicatorPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicatorPrefab.transform.localScale = Vector3.one * 0.05f;
        indicatorPrefab.name = "ARPlaneIndicator";
        indicatorPrefab.SetActive(false);
    }
    
    private void OnEnable()
    {
        planeManager.planesChanged += OnPlanesChanged;
    }
    
    private void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
        CleanupAllIndicators();
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Handle added planes
        foreach (ARPlane plane in args.added)
        {
            if (draw3DIndicators)
            {
                CreateIndicatorsForPlane(plane);
            }
        }
        
        // Handle updated planes
        foreach (ARPlane plane in args.updated)
        {
            // Update visualizers
            if (draw3DIndicators)
            {
                UpdateIndicatorsForPlane(plane);
            }
        }
        
        // Handle removed planes
        foreach (ARPlane plane in args.removed)
        {
            if (draw3DIndicators)
            {
                CleanupIndicatorsForPlane(plane.trackableId);
            }
        }
    }
    
    private void CreateIndicatorsForPlane(ARPlane plane)
    {
        if (plane == null || indicatorPrefab == null) return;
        
        List<GameObject> indicators = new List<GameObject>();
        
        // Create indicators at plane boundary vertices
        ARPlaneMeshVisualizer meshVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (meshVisualizer != null && meshVisualizer.mesh != null)
        {
            Vector3[] vertices = meshVisualizer.mesh.vertices;
            
            // Create a reasonable number of indicators (max 8)
            int step = Mathf.Max(1, vertices.Length / 8);
            for (int i = 0; i < vertices.Length; i += step)
            {
                GameObject indicator = Instantiate(indicatorPrefab, plane.transform);
                indicator.SetActive(true);
                indicator.transform.localPosition = vertices[i];
                
                // Set color based on plane alignment
                Renderer renderer = indicator.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical ? 
                                             verticalPlaneColor : horizontalPlaneColor;
                }
                
                indicators.Add(indicator);
            }
            
            // Also add center indicator
            GameObject centerIndicator = Instantiate(indicatorPrefab, plane.transform);
            centerIndicator.SetActive(true);
            centerIndicator.transform.localPosition = Vector3.zero;
            centerIndicator.transform.localScale = Vector3.one * 0.08f;  // Make center indicator larger
            
            // Set center indicator color
            Renderer centerRenderer = centerIndicator.GetComponent<Renderer>();
            if (centerRenderer != null)
            {
                centerRenderer.material.color = Color.red;
            }
            
            indicators.Add(centerIndicator);
        }
        
        planeIndicators[plane.trackableId] = indicators;
    }
    
    private void UpdateIndicatorsForPlane(ARPlane plane)
    {
        // Remove old indicators and create new ones
        if (planeIndicators.ContainsKey(plane.trackableId))
        {
            CleanupIndicatorsForPlane(plane.trackableId);
        }
        
        CreateIndicatorsForPlane(plane);
    }
    
    private void CleanupIndicatorsForPlane(TrackableId id)
    {
        if (planeIndicators.TryGetValue(id, out List<GameObject> indicators))
        {
            foreach (GameObject indicator in indicators)
            {
                if (indicator != null)
                {
                    Destroy(indicator);
                }
            }
            
            planeIndicators.Remove(id);
        }
    }
    
    private void CleanupAllIndicators()
    {
        foreach (var idAndIndicators in planeIndicators)
        {
            foreach (GameObject indicator in idAndIndicators.Value)
            {
                if (indicator != null)
                {
                    Destroy(indicator);
                }
            }
        }
        
        planeIndicators.Clear();
    }
    
    private void OnDrawGizmos()
    {
        if (!drawBoundaries || planeManager == null) return;
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane.subsumedBy != null) continue; // Skip subsumed planes
            
            // Set color based on plane alignment
            Gizmos.color = plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical ? 
                          verticalPlaneColor : horizontalPlaneColor;
            
            // Get mesh visualizer component
            ARPlaneMeshVisualizer meshVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (meshVisualizer != null && meshVisualizer.mesh != null)
            {
                // Draw outline
                Vector3[] vertices = meshVisualizer.mesh.vertices;
                int[] triangles = meshVisualizer.mesh.triangles;
                
                HashSet<Vector2Int> drawnEdges = new HashSet<Vector2Int>();
                
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    DrawTriangleEdges(plane.transform, 
                                      vertices[triangles[i]], 
                                      vertices[triangles[i + 1]], 
                                      vertices[triangles[i + 2]],
                                      triangles[i],
                                      triangles[i + 1],
                                      triangles[i + 2],
                                      drawnEdges);
                }
            }
        }
    }
    
    private void DrawTriangleEdges(Transform planeTransform, 
                                  Vector3 v1, Vector3 v2, Vector3 v3,
                                  int i1, int i2, int i3,
                                  HashSet<Vector2Int> drawnEdges)
    {
        // Draw line between vertices if it's an outer edge
        DrawEdgeIfNeeded(planeTransform, v1, v2, i1, i2, drawnEdges);
        DrawEdgeIfNeeded(planeTransform, v2, v3, i2, i3, drawnEdges);
        DrawEdgeIfNeeded(planeTransform, v3, v1, i3, i1, drawnEdges);
    }
    
    private void DrawEdgeIfNeeded(Transform planeTransform, 
                                 Vector3 start, Vector3 end, 
                                 int startIndex, int endIndex,
                                 HashSet<Vector2Int> drawnEdges)
    {
        // Create an edge identifier (always put smaller index first to avoid duplicates)
        Vector2Int edge = startIndex < endIndex ? 
                         new Vector2Int(startIndex, endIndex) : 
                         new Vector2Int(endIndex, startIndex);
                         
        // Draw edge if not drawn already
        if (!drawnEdges.Contains(edge))
        {
            Vector3 worldStart = planeTransform.TransformPoint(start);
            Vector3 worldEnd = planeTransform.TransformPoint(end);
            
            Gizmos.DrawLine(worldStart, worldEnd);
            drawnEdges.Add(edge);
        }
    }
} 