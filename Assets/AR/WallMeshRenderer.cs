using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;

/// <summary>
/// Integrates AR Mesh Manager with EnhancedDeepLabPredictor for walls visualization.
/// </summary>
[RequireComponent(typeof(ARMeshManager))]
public class WallMeshRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARCameraManager _arCameraManager;
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private ARPlaneManager _arPlaneManager; // AR Plane Manager for detection

    [Header("Wall Detection Settings")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float _verticalThreshold = 0.6f;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float _wallConfidenceThreshold = 0.05f; // Lowered threshold for better detection
    [SerializeField] private float _updateInterval = 0.2f; // More frequent updates
    [SerializeField] public bool _onlyUseVerticalPlanes = true; // Filter only vertical planes

    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private bool _showAllMeshes = true; // Show all meshes by default
    [SerializeField] private bool _logMeshCounts = true;
    [SerializeField] private Color _debugMeshColor = new Color(1.0f, 0.0f, 1.0f, 0.7f); // Bright magenta with transparency
    [SerializeField] private bool _showDebugVisualizer = true;
    [SerializeField] private float _debugVisualizerScale = 0.5f; // Larger visualizer

    // Public properties
    public ARCameraManager ARCameraManager 
    { 
        get { return _arCameraManager; } 
        set { _arCameraManager = value; }
    }
    
    public EnhancedDeepLabPredictor Predictor 
    { 
        get { return _predictor; } 
        set { _predictor = value; }
    }
    
    public Material WallMaterial 
    { 
        get { return _wallMaterial; } 
        set { _wallMaterial = value; }
    }
    
    public float VerticalThreshold 
    { 
        get { return _verticalThreshold; } 
        set { _verticalThreshold = value; }
    }
    
    public float WallConfidenceThreshold 
    { 
        get { return _wallConfidenceThreshold; } 
        set { _wallConfidenceThreshold = value; }
    }
    
    public bool ShowDebugInfo 
    { 
        get { return _showDebugInfo; } 
        set { _showDebugInfo = value; }
    }
    
    public bool ShowAllMeshes 
    { 
        get { return _showAllMeshes; } 
        set { _showAllMeshes = value; }
    }

    private ARMeshManager _meshManager;
    private Dictionary<GameObject, bool> _meshObjectState = new Dictionary<GameObject, bool>();
    private Dictionary<GameObject, MeshRenderer> _meshRenderers = new Dictionary<GameObject, MeshRenderer>();
    private Texture2D _segmentationTexture;
    private bool _predictor_isReady = false;
    private bool _isUpdatingMeshes = false;
    [SerializeField] public byte _wallClassId = 9;  // Ensure consistent wall class ID (ADE20K)

    private void Awake()
    {
        _meshManager = GetComponent<ARMeshManager>();
        
        if (_wallMaterial == null)
        {
            // Create a default material for walls if not already assigned
            _wallMaterial = new Material(Shader.Find("Standard"));
            _wallMaterial.color = _debugMeshColor;
            
            if (_showDebugInfo)
            {
                Debug.Log("WallMeshRenderer: Created default wall material");
            }
        }
        
        // Find ARPlaneManager if not assigned
        if (_arPlaneManager == null)
        {
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            if (_arPlaneManager != null && _showDebugInfo)
            {
                Debug.Log("WallMeshRenderer: Found ARPlaneManager");
            }
        }
    }

    private void OnEnable()
    {
        if (_meshManager != null)
        {
            _meshManager.meshesChanged += OnMeshesChanged;
            
            if (_showDebugInfo)
            {
                Debug.Log("WallMeshRenderer: Subscribed to meshesChanged events");
            }
        }
        else
        {
            Debug.LogError("WallMeshRenderer: ARMeshManager component is missing!");
        }

        // Subscribe to the predictor segmentation events if available
        if (_predictor != null)
        {
            try
            {
                Type predictorType = _predictor.GetType();
                var segmentationEvent = predictorType.GetField("OnSegmentationCompleted");
                
                if (segmentationEvent != null)
                {
                    // For EnhancedDeepLabPredictor
                    _predictor.OnSegmentationCompleted += OnSegmentationCompleted;
                    _predictor_isReady = true;
                    Debug.Log($"WallMeshRenderer: Using EnhancedDeepLabPredictor with wall class ID: {_wallClassId}");
                    
                    // Subscribe to wall class ID changes
                    _predictor.OnWallClassIdChanged += OnPredictorWallClassIdChanged;
                    
                    // Force sync with current predictor values
                    _wallClassId = (byte)_predictor.WallClassId;
                    Debug.Log($"WallMeshRenderer: Synced wall class ID: {_wallClassId}");
                    
                    // Show current wall confidence threshold
                    Debug.Log($"WallMeshRenderer: Current wall confidence threshold: {_wallConfidenceThreshold}");
                }
                else
                {
                    Debug.LogWarning("WallMeshRenderer: Could not find OnSegmentationCompleted event on predictor");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"WallMeshRenderer: Error subscribing to segmentation events: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("WallMeshRenderer: No DeepLabPredictor assigned!");
        }

        // Start the update coroutine
        StartCoroutine(UpdateMeshesRoutine());
        
        // Show debug visualizer if enabled
        if (_showDebugVisualizer)
        {
            Debug.Log("WallMeshRenderer: Enabling debug visualizer on startup");
            ToggleDebugVisualizer();
        }
    }

    private void OnDisable()
    {
        if (_meshManager != null)
        {
            _meshManager.meshesChanged -= OnMeshesChanged;
        }

        if (_predictor != null && _predictor_isReady)
        {
            _predictor.OnSegmentationCompleted -= OnSegmentationCompleted;
            _predictor.OnWallClassIdChanged -= OnPredictorWallClassIdChanged;
        }

        StopAllCoroutines();
    }

    private void OnSegmentationCompleted(Texture2D segmentationTexture)
    {
        _segmentationTexture = segmentationTexture;
        
        if (_showDebugInfo && _logMeshCounts)
        {
            Debug.Log("WallMeshRenderer: Received new segmentation texture");
        }
        
        // If we're showing debug visualizer, create a quad to display the texture
        if (_showDebugVisualizer)
        {
            ShowDebugTexture(segmentationTexture);
        }
        
        // Immediately analyze meshes to show detection results faster
        if (!_isUpdatingMeshes)
        {
            _isUpdatingMeshes = true;
            try
            {
                AnalyzeMeshes();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"WallMeshRenderer: Error analyzing meshes: {e.Message}");
            }
            _isUpdatingMeshes = false;
        }
    }

    private void ShowDebugTexture(Texture2D texture)
    {
        // Find or create debug visualizer
        Transform visualizer = transform.Find("DebugVisualizer");
        if (visualizer == null)
        {
            GameObject visualizerObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visualizerObj.name = "DebugVisualizer";
            visualizerObj.transform.SetParent(transform);
            
            // Position it in front of the camera
            if (_arCameraManager != null)
            {
                Camera camera = _arCameraManager.GetComponent<Camera>();
                if (camera != null)
                {
                    // Position closer to camera for better visibility
                    visualizerObj.transform.position = camera.transform.position + camera.transform.forward * 0.3f;
                    visualizerObj.transform.rotation = camera.transform.rotation;
                }
            }
            
            // Scale it appropriately
            visualizerObj.transform.localScale = new Vector3(_debugVisualizerScale, _debugVisualizerScale, _debugVisualizerScale);
            
            // Create material with transparent shader
            Material mat = new Material(Shader.Find("Unlit/Transparent"));
            if (mat != null)
            {
                visualizerObj.GetComponent<MeshRenderer>().material = mat;
            }
            else
            {
                // Fallback to standard unlit
                mat = new Material(Shader.Find("Unlit/Texture"));
                visualizerObj.GetComponent<MeshRenderer>().material = mat;
            }
            
            visualizer = visualizerObj.transform;
            
            Debug.Log("WallMeshRenderer: Created debug visualizer");
        }
        else
        {
            // Update position for existing visualizer
            if (_arCameraManager != null)
            {
                Camera camera = _arCameraManager.GetComponent<Camera>();
                if (camera != null)
                {
                    visualizer.position = camera.transform.position + camera.transform.forward * 0.3f;
                    visualizer.rotation = camera.transform.rotation;
                }
            }
        }
        
        // Update the texture
        MeshRenderer renderer = visualizer.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.mainTexture = texture;
            renderer.enabled = true;
        }
    }

    private void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        if (_showDebugInfo && _logMeshCounts)
        {
            Debug.Log($"WallMeshRenderer: Meshes changed - Added: {args.added.Count}, Updated: {args.updated.Count}, Removed: {args.removed.Count}");
        }

        // Process added meshes
        foreach (var meshFilter in args.added)
        {
            if (meshFilter == null) continue;
            
            GameObject meshObj = meshFilter.gameObject;
            
            // Add to our tracking dictionaries if not already there
            if (!_meshObjectState.ContainsKey(meshObj))
            {
                _meshObjectState[meshObj] = false; // Initially hidden
                
                // Get or add mesh renderer
                MeshRenderer renderer = meshObj.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    renderer = meshObj.AddComponent<MeshRenderer>();
                }
                
                // Set the wall material
                renderer.material = _wallMaterial;
                
                // Initially hide the mesh
                renderer.enabled = _showAllMeshes;
                
                _meshRenderers[meshObj] = renderer;
            }
        }

        // Remove deleted meshes from our tracking
        foreach (var meshFilter in args.removed)
        {
            if (meshFilter == null) continue;
            
            GameObject meshObj = meshFilter.gameObject;
            _meshObjectState.Remove(meshObj);
            _meshRenderers.Remove(meshObj);
        }
    }

    /// <summary>
    /// Check if a plane is vertical (wall)
    /// </summary>
    /// <param name="plane">AR plane to check</param>
    /// <returns>True if plane is vertical</returns>
    private bool IsVerticalPlane(ARPlane plane)
    {
        if (plane == null) return false;
        
        // Check alignment directly
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            return true;
        }
        
        // Additional check for normal direction
        Vector3 normal = plane.normal;
        float dotWithUp = Vector3.Dot(normal, Vector3.up);
        
        // If the normal is close to horizontal (perpendicular to up vector), it's a vertical plane
        return Mathf.Abs(dotWithUp) < _verticalThreshold;
    }
    
    private IEnumerator UpdateMeshesRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(_updateInterval);
            
            if (_segmentationTexture != null && _predictor_isReady && !_isUpdatingMeshes)
            {
                _isUpdatingMeshes = true;
                AnalyzeMeshes();
                _isUpdatingMeshes = false;
            }
        }
    }

    private void AnalyzeMeshes()
    {
        int totalMeshes = _meshObjectState.Count;
        int verticalMeshes = 0;
        int wallMeshes = 0;
        
        foreach (var kvp in _meshObjectState)
        {
            GameObject meshObj = kvp.Key;
            if (meshObj == null) continue;
            
            MeshFilter meshFilter = meshObj.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.mesh == null) continue;
            
            // Check if this mesh is vertical
            bool isVertical = IsMeshVertical(meshFilter.mesh);
            
            if (isVertical)
            {
                verticalMeshes++;
                
                // For vertical meshes, check if they are walls according to the segmentation
                bool isWall = IsMeshWall(meshObj);
                
                // If using AR planes with planeManager, also check for plane match
                bool matchesARPlane = false;
                if (_arPlaneManager != null && _onlyUseVerticalPlanes)
                {
                    matchesARPlane = IsMeshMatchingVerticalARPlane(meshObj);
                    
                    // Only consider as wall if it matches both segmentation and AR plane detection
                    isWall = isWall && matchesARPlane;
                }
                
                if (isWall)
                {
                    wallMeshes++;
                    
                    // Update mesh visibility if needed
                    if (!kvp.Value)
                    {
                        _meshObjectState[meshObj] = true;
                        if (_meshRenderers.TryGetValue(meshObj, out var renderer))
                        {
                            renderer.enabled = true;
                        }
                    }
                }
                else if (!_showAllMeshes && kvp.Value)
                {
                    // Hide non-wall vertical meshes
                    _meshObjectState[meshObj] = false;
                    if (_meshRenderers.TryGetValue(meshObj, out var renderer))
                    {
                        renderer.enabled = false;
                    }
                }
            }
            else if (!_showAllMeshes && kvp.Value)
            {
                // Hide non-vertical meshes
                _meshObjectState[meshObj] = false;
                if (_meshRenderers.TryGetValue(meshObj, out var renderer))
                {
                    renderer.enabled = false;
                }
            }
        }
        
        if (_showDebugInfo && _logMeshCounts)
        {
            Debug.Log($"WallMeshRenderer: Analyzed {totalMeshes} meshes, {verticalMeshes} vertical, {wallMeshes} walls");
        }
    }
    
    /// <summary>
    /// Check if a mesh overlaps with a vertical AR plane
    /// </summary>
    private bool IsMeshMatchingVerticalARPlane(GameObject meshObj)
    {
        if (meshObj == null || _arPlaneManager == null) return false;
        
        // Get mesh bounds in world space
        MeshFilter meshFilter = meshObj.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null) return false;
        
        Bounds meshBounds = new Bounds(
            meshObj.transform.TransformPoint(meshFilter.mesh.bounds.center),
            Vector3.Scale(meshFilter.mesh.bounds.size, meshObj.transform.lossyScale)
        );
        
        // Check all tracked planes
        foreach (ARPlane plane in _arPlaneManager.trackables)
        {
            // Skip non-vertical planes
            if (!IsVerticalPlane(plane)) continue;
            
            // Create approximate bounds for the plane
            Vector3 planeCenter = plane.transform.position;
            Vector3 planeSize = new Vector3(plane.size.x, plane.size.y, 0.05f);
            Bounds planeBounds = new Bounds(planeCenter, planeSize);
            
            // Check if bounds overlap
            if (meshBounds.Intersects(planeBounds))
            {
                if (_showDebugInfo)
                {
                    Debug.Log($"WallMeshRenderer: Mesh matches vertical AR plane at {planeCenter}");
                }
                return true;
            }
        }
        
        return false;
    }

    private bool IsMeshVertical(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount == 0)
            return false;

        // Get the mesh normals to analyze orientation
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length == 0)
            return false;

        int verticalCount = 0;
        
        for (int i = 0; i < normals.Length; i++)
        {
            // Check if normal is mostly horizontal (which means the surface is vertical)
            if (Mathf.Abs(normals[i].y) < _verticalThreshold)
            {
                verticalCount++;
            }
        }
        
        // Consider the mesh vertical if most of the normals are vertical
        float verticalRatio = (float)verticalCount / normals.Length;
        return verticalRatio > 0.5f;
    }

    private bool IsMeshWall(GameObject meshObj)
    {
        if (_segmentationTexture == null || _arCameraManager == null || meshObj == null)
            return false;
            
        // Get the position of the mesh in screen space
        Camera arCamera = _arCameraManager.GetComponent<Camera>();
        if (arCamera == null)
            return false;
            
        // Use the mesh center as reference point
        MeshFilter meshFilter = meshObj.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
            return false;
            
        // Get the center of the mesh's bounds in world space
        Vector3 meshCenter = meshObj.transform.TransformPoint(meshFilter.mesh.bounds.center);
        
        // Project the mesh center to screen space
        Vector2 screenPos = arCamera.WorldToScreenPoint(meshCenter);
        
        // Convert to the texture's coordinate system
        int x = Mathf.Clamp(Mathf.RoundToInt(screenPos.x * _segmentationTexture.width / Screen.width), 0, _segmentationTexture.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(screenPos.y * _segmentationTexture.height / Screen.height), 0, _segmentationTexture.height - 1);
        
        // Get the color at this position
        Color pixel = _segmentationTexture.GetPixel(x, y);
        
        // Check all channels to find wall pixels
        bool isWall = false;
        
        // Method 1: Check if red channel contains the wall class ID (normalized value)
        float normalizedClassId = _wallClassId / 255.0f;
        if (Mathf.Abs(pixel.r - normalizedClassId) < 0.1f) {
            isWall = true;
        }
        
        // Method 2: Check if any channel is high enough for wall confidence
        if (!isWall && (pixel.r > _wallConfidenceThreshold || pixel.g > _wallConfidenceThreshold || pixel.b > _wallConfidenceThreshold)) {
            isWall = true;
        }
        
        // Method 3: More comprehensive check using multiple sample points
        if (!isWall)
        {
            // Sample a few points in the mesh bounds to check for wall pixels
            Vector3[] samplePoints = GetMeshSamplePoints(meshObj, meshFilter.mesh, 5);
            int wallPixelCount = 0;
            
            foreach (Vector3 point in samplePoints)
            {
                Vector2 sampleScreenPos = arCamera.WorldToScreenPoint(point);
                
                int sx = Mathf.Clamp(Mathf.RoundToInt(sampleScreenPos.x * _segmentationTexture.width / Screen.width), 0, _segmentationTexture.width - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(sampleScreenPos.y * _segmentationTexture.height / Screen.height), 0, _segmentationTexture.height - 1);
                
                Color samplePixel = _segmentationTexture.GetPixel(sx, sy);
                
                if (Mathf.Abs(samplePixel.r - normalizedClassId) < 0.1f || 
                    samplePixel.r > _wallConfidenceThreshold || 
                    samplePixel.g > _wallConfidenceThreshold || 
                    samplePixel.b > _wallConfidenceThreshold)
                {
                    wallPixelCount++;
                }
            }
            
            // If more than half of the sample points are wall pixels, consider this a wall
            if (wallPixelCount > samplePoints.Length / 2)
            {
                isWall = true;
            }
        }
        
        if (_showDebugInfo && isWall) {
            Debug.Log($"WallMeshRenderer: Wall detected at pixel {x},{y} = R:{pixel.r:F2} G:{pixel.g:F2} B:{pixel.b:F2}");
        }
        
        return isWall;
    }
    
    private Vector3[] GetMeshSamplePoints(GameObject meshObj, Mesh mesh, int sampleCount)
    {
        Vector3[] samplePoints = new Vector3[sampleCount];
        
        // Get the bounds
        Bounds bounds = mesh.bounds;
        Vector3 center = meshObj.transform.TransformPoint(bounds.center);
        Vector3 extents = Vector3.Scale(bounds.extents, meshObj.transform.lossyScale);
        
        // Add center point
        samplePoints[0] = center;
        
        // Add points distributed within the bounds
        for (int i = 1; i < sampleCount; i++)
        {
            float x = center.x + (((i % 3) - 1) * extents.x * 0.5f);
            float y = center.y + (((i / 3) % 3 - 1) * extents.y * 0.5f);
            float z = center.z + (((i / 9) % 3 - 1) * extents.z * 0.5f);
            
            samplePoints[i] = new Vector3(x, y, z);
        }
        
        return samplePoints;
    }

    // Public method to force the mesh update (can be called from editor or other scripts)
    public void ForceUpdateMeshes()
    {
        if (_isUpdatingMeshes) return;
        
        if (_segmentationTexture != null && _predictor_isReady)
        {
            _isUpdatingMeshes = true;
            AnalyzeMeshes();
            _isUpdatingMeshes = false;
            
            Debug.Log("WallMeshRenderer: Forced mesh update completed");
        }
        else
        {
            Debug.LogWarning("WallMeshRenderer: Cannot force update - segmentation data not available yet");
        }
    }

    public void ToggleShowAllMeshes()
    {
        _showAllMeshes = !_showAllMeshes;
        
        // Update all mesh renderers immediately
        foreach (var kvp in _meshRenderers)
        {
            MeshRenderer renderer = kvp.Value;
            if (renderer != null)
            {
                renderer.enabled = _showAllMeshes || (_meshObjectState.ContainsKey(kvp.Key) && _meshObjectState[kvp.Key]);
            }
        }
        
        Debug.Log($"WallMeshRenderer: Show all meshes set to {_showAllMeshes}");
        
        // Force an update
        ForceUpdateMeshes();
    }

    public void ToggleDebugVisualizer()
    {
        _showDebugVisualizer = !_showDebugVisualizer;
        
        // Show/hide the visualizer
        Transform visualizer = transform.Find("DebugVisualizer");
        if (visualizer != null)
        {
            visualizer.gameObject.SetActive(_showDebugVisualizer);
        }
        else if (_showDebugVisualizer && _segmentationTexture != null)
        {
            ShowDebugTexture(_segmentationTexture);
        }
        
        Debug.Log($"WallMeshRenderer: Debug visualizer set to {_showDebugVisualizer}");
    }

    private void OnPredictorWallClassIdChanged(byte newWallClassId)
    {
        _wallClassId = newWallClassId;
        UpdateMeshColors();
    }

    private void UpdateMeshColors()
    {
        // Implementation of UpdateMeshColors method
    }
} 