using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;

public class SurfaceVisualizer : MonoBehaviour
{
    [SerializeField] private ARPlaneManager _planeManager;
    
    // Dictionary to track visualized planes
    private Dictionary<TrackableId, GameObject> _visualizedPlanes = new Dictionary<TrackableId, GameObject>();
    
    [SerializeField] private Material _planeMaterial;
    [SerializeField] private Color _planeColor = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private float _planeLineWidth = 0.05f;

    private void OnEnable()
    {
        if (_planeManager != null)
        {
            _planeManager.planesChanged += OnPlanesChanged;
        }
    }

    private void OnDisable()
    {
        if (_planeManager != null)
        {
            _planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        foreach (ARPlane plane in args.added)
        {
            if (ShouldVisualizePlane(plane))
            {
                AddPlaneToVisualization(plane);
            }
        }

        foreach (ARPlane plane in args.updated)
        {
            if (ShouldVisualizePlane(plane))
            {
                if (_visualizedPlanes.ContainsKey(plane.trackableId))
                {
                    UpdatePlaneVisualization(plane);
                }
                else
                {
                    AddPlaneToVisualization(plane);
                }
            }
            else if (_visualizedPlanes.ContainsKey(plane.trackableId))
            {
                RemovePlaneVisualization(plane);
            }
        }

        foreach (ARPlane plane in args.removed)
        {
            RemovePlaneVisualization(plane);
        }
    }

    private bool ShouldVisualizePlane(ARPlane plane)
    {
        // You can add more complex logic here if needed
        // For example, you might only want to visualize horizontal planes
        return plane.trackingState == TrackingState.Tracking && _planeManager.enabled;
    }

    private void AddPlaneToVisualization(ARPlane plane)
    {
        GameObject visualPlane = new GameObject($"VisualizedPlane_{plane.trackableId}");
        visualPlane.transform.SetParent(transform);
        
        LineRenderer lineRenderer = visualPlane.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = false;
        lineRenderer.material = _planeMaterial ?? new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineRenderer.startColor = lineRenderer.endColor = _planeColor;
        lineRenderer.startWidth = lineRenderer.endWidth = _planeLineWidth;
        lineRenderer.loop = true;
        
        UpdateLinePoints(lineRenderer, plane);
        
        _visualizedPlanes.Add(plane.trackableId, visualPlane);
    }

    private void UpdatePlaneVisualization(ARPlane plane)
    {
        if (_visualizedPlanes.TryGetValue(plane.trackableId, out GameObject visualPlane))
        {
            LineRenderer lineRenderer = visualPlane.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                UpdateLinePoints(lineRenderer, plane);
            }
            
            // Position the visualization at the plane's center
            visualPlane.transform.position = plane.center;
            visualPlane.transform.rotation = plane.transform.rotation;
        }
    }

    private void RemovePlaneVisualization(ARPlane plane)
    {
        if (_visualizedPlanes.TryGetValue(plane.trackableId, out GameObject visualPlane))
        {
            Destroy(visualPlane);
            _visualizedPlanes.Remove(plane.trackableId);
        }
    }
    
    private void UpdateLinePoints(LineRenderer lineRenderer, ARPlane plane)
    {
        if (plane.boundary.Length < 2)
            return;
            
        // Set the number of points
        lineRenderer.positionCount = plane.boundary.Length;
        
        // Set the points from the plane's boundary
        for (int i = 0; i < plane.boundary.Length; i++)
        {
            lineRenderer.SetPosition(i, new Vector3(plane.boundary[i].x, 0, plane.boundary[i].y));
        }
    }
} 