using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

/// <summary>
/// Helper for configuring ARPlaneManager for better wall visualization
/// </summary>
public class ARPlaneHelper : MonoBehaviour
{
    [Header("Plane Settings")]
    [SerializeField] private Material wallPlaneMaterial;
    [SerializeField] private Color wallPlaneColor = new Color(1.0f, 0.0f, 1.0f, 1.0f); // Bright magenta for high visibility
    [SerializeField] private float planeOpacity = 0.9f; // Higher opacity for better visibility
    
    [Header("Plane Filtering")]
    [SerializeField] private bool showOnlyVerticals = true;
    [SerializeField] private bool hideHorizontals = true;
    
    private ARPlaneManager _planeManager;
    
    private void Awake()
    {
        // Find ARPlaneManager in scene
        _planeManager = FindFirstObjectByType<ARPlaneManager>();
        
        if (_planeManager == null)
        {
            Debug.LogWarning("ARPlaneHelper: ARPlaneManager not found in scene!");
            return;
        }
        
        // Configure material for planes
        ConfigurePlaneMaterial();
        
        // Subscribe to plane detection event
        _planeManager.planesChanged += OnPlanesChanged;
        
        // Check detection mode
        if (showOnlyVerticals)
        {
            _planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
            Debug.Log("ARPlaneHelper: Set plane detection mode to Vertical only");
        }
        else
        {
            _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            Debug.Log("ARPlaneHelper: Set plane detection mode to Horizontal and Vertical");
        }
    }
    
    private void OnDestroy()
    {
        if (_planeManager != null)
        {
            _planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    /// <summary>
    /// Configures materials for planes
    /// </summary>
    private void ConfigurePlaneMaterial()
    {
        if (_planeManager == null) return;
        
        // Create material for walls if not assigned
        if (wallPlaneMaterial == null)
        {
            // Use Unlit/Color shader for maximum visibility
            wallPlaneMaterial = new Material(Shader.Find("Unlit/Color"));
            if (wallPlaneMaterial.shader == null)
            {
                // Fallback to Standard if Unlit/Color is not available
                wallPlaneMaterial = new Material(Shader.Find("Standard"));
            }
            
            wallPlaneMaterial.color = wallPlaneColor;
            
            // Try to add transparency if using Standard shader
            if (wallPlaneMaterial.shader.name == "Standard")
            {
                // Configure transparency
                wallPlaneMaterial.SetFloat("_Mode", 3); // Transparent mode
                wallPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                wallPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                wallPlaneMaterial.SetInt("_ZWrite", 0);
                wallPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
                wallPlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                wallPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                wallPlaneMaterial.renderQueue = 3000;
            }
            
            Debug.Log("ARPlaneHelper: Created new wall plane material with bright color");
        }
        
        // Apply material to ARPlaneManager
        if (_planeManager.planePrefab != null)
        {
            var meshRenderer = _planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.material = wallPlaneMaterial;
                
                // Create a new default plane prefab if none exists
                if (_planeManager.planePrefab == null)
                {
                    Debug.Log("ARPlaneHelper: No plane prefab found, creating a default one");
                    GameObject planePrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    planePrefab.name = "AR Plane Visual";
                    MeshRenderer prefabRenderer = planePrefab.GetComponent<MeshRenderer>();
                    prefabRenderer.material = wallPlaneMaterial;
                    _planeManager.planePrefab = planePrefab;
                }
                
                Debug.Log("ARPlaneHelper: Updated plane prefab material");
            }
            else
            {
                Debug.LogWarning("ARPlaneHelper: Plane prefab has no MeshRenderer");
            }
        }
        else
        {
            Debug.LogWarning("ARPlaneHelper: ARPlaneManager has no plane prefab assigned");
        }
    }
    
    /// <summary>
    /// Handler for plane detection event
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Handle added planes
        foreach (var plane in args.added)
        {
            ProcessPlane(plane);
        }

        // Handle updated planes
        foreach (var plane in args.updated)
        {
            ProcessPlane(plane);
        }
    }
    
    /// <summary>
    /// Updates plane visibility based on its orientation
    /// </summary>
    private void ProcessPlane(ARPlane plane)
    {
        if (plane == null) return;
        
        bool isVertical = plane.alignment == PlaneAlignment.Vertical;
        
        // Update material based on plane type
        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // Set color based on orientation
            if (isVertical)
            {
                // For vertical planes (walls) use our material
                renderer.material = wallPlaneMaterial;
                renderer.material.color = wallPlaneColor;
                
                // Always show vertical planes
                renderer.enabled = true;
                
                // Force update material
                if (renderer.material != wallPlaneMaterial)
                {
                    renderer.material = wallPlaneMaterial;
                }
                
                Debug.Log($"ARPlaneHelper: Vertical plane found and enabled - ID: {plane.trackableId}");
            }
            else
            {
                // For horizontal planes
                if (hideHorizontals)
                {
                    // Hide horizontal planes if specified
                    renderer.enabled = false;
                }
                else
                {
                    // Otherwise show with a different color
                    renderer.material.color = new Color(0.7f, 0.7f, 0.7f, planeOpacity * 0.7f);
                    renderer.enabled = true;
                }
            }
        }
        
        // Update boundary line visibility
        LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.enabled = isVertical || !hideHorizontals;
            
            if (isVertical)
            {
                // Use brighter boundary color for walls
                lineRenderer.startColor = new Color(1.0f, 0.0f, 1.0f, 1.0f);
                lineRenderer.endColor = new Color(1.0f, 0.0f, 1.0f, 1.0f);
                lineRenderer.startWidth = 0.03f;
                lineRenderer.endWidth = 0.03f;
            }
        }
    }
    
    /// <summary>
    /// Helper method to find component in scene
    /// </summary>
    public new static T FindFirstObjectByType<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>();
    }
    
    public void ForceUpdatePlanes()
    {
        if (_planeManager == null) return;
        
        // Find all existing planes and update their visibility
        ARPlane[] planes = FindObjectsByType<ARPlane>(FindObjectsSortMode.None);
        foreach (ARPlane plane in planes)
        {
            ProcessPlane(plane);
        }
        
        Debug.Log($"ARPlaneHelper: Force updated {planes.Length} planes");
    }
} 