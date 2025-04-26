using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor
using Unity.XR.CoreUtils;

/// <summary>
/// Component that integrates AR plane detection with wall segmentation for accurate wall painting
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARWallPainter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public EnhancedDeepLabPredictor _predictor;
    [SerializeField] public ARCameraManager _cameraManager;
    [SerializeField] public Material _wallMaterial;
    
    [Header("Wall Detection Settings")]
    [SerializeField] public float _wallConfidenceThreshold = 0.3f;
    [SerializeField] public byte _wallClassId = 9; // ADE20K wall class ID
    [SerializeField] private float _planeUpdateInterval = 0.5f;
    [SerializeField] private bool _debugMode = false;
    [SerializeField] private int _samplePointCount = 3;
    
    // AR components
    private ARPlaneManager _planeManager;
    private Dictionary<TrackableId, Material> _wallPlaneMaterials = new Dictionary<TrackableId, Material>();
    
    // Segmentation data
    private Texture2D _segmentationTexture;
    private bool _hasNewSegmentation = false;
    private float _lastPlaneUpdateTime = 0f;
    
    // Cache camera reference
    private Camera _arCamera;
    // Cache calculation variables
    private float _normalizedClassId;
    
    // Performance optimization - skip update frames
    private int _frameCounter = 0;
    private int _updateInterval = 5; // Only update on every 5th frame
    
    // ID cache to avoid reprocessing planes
    private HashSet<TrackableId> _processedPlanes = new HashSet<TrackableId>();
    private HashSet<TrackableId> _knownWallPlanes = new HashSet<TrackableId>();
    
    private void Awake()
    {
        // Get required components
        _planeManager = GetComponent<ARPlaneManager>();
        
        if (_cameraManager == null)
            _cameraManager = FindObjectOfType<ARCameraManager>();
            
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
        // Create default wall material if not assigned
        if (_wallMaterial == null)
        {
            _wallMaterial = new Material(Shader.Find("Standard"));
            _wallMaterial.color = new Color(0.5f, 0.8f, 1f, 0.7f);
            
            // Setup for transparency
            _wallMaterial.SetFloat("_Mode", 3); // Transparent mode
            _wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _wallMaterial.SetInt("_ZWrite", 0);
            _wallMaterial.DisableKeyword("_ALPHATEST_ON");
            _wallMaterial.EnableKeyword("_ALPHABLEND_ON");
            _wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _wallMaterial.renderQueue = 3000;
            
            if (_debugMode)
                Debug.Log("ARWallPainter: Created default wall material");
        }
        
        // Precalculate values
        _normalizedClassId = _wallClassId / 255.0f;
    }
    
    private void OnEnable()
    {
        // Get plane manager if not assigned
        if (_planeManager == null)
        {
            _planeManager = GetComponent<ARPlaneManager>();
        }
        
        // Subscribe to plane changes
        if (_planeManager != null)
        {
            _planeManager.planesChanged += OnPlanesChanged;
        }
        
        // Get AR camera reference
        if (_arCamera == null)
        {
            var sessionOrigin = FindAnyObjectByType<XROrigin>();
            if (sessionOrigin != null && sessionOrigin.Camera != null)
            {
                _arCamera = sessionOrigin.Camera;
            }
        }
        
        // Subscribe to segmentation events
        // We're using direct event subscription in this sample
    }
    
    private void OnDisable()
    {
        // Unsubscribe from plane changes
        if (_planeManager != null)
        {
            _planeManager.planesChanged -= OnPlanesChanged;
        }
        
        // Clean up materials to avoid memory leaks
        foreach (var material in _wallPlaneMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        _wallPlaneMaterials.Clear();
    }
    
    private void Update()
    {
        // Increment frame counter
        _frameCounter++;
        
        // Check if it's time to update plane materials
        if (_hasNewSegmentation && 
            Time.time - _lastPlaneUpdateTime > _planeUpdateInterval &&
            _frameCounter % _updateInterval == 0)
        {
            UpdateWallPlanes();
            _lastPlaneUpdateTime = Time.time;
            _hasNewSegmentation = false;
        }
    }
    
    private void OnSegmentationCompleted(Texture2D segmentationTexture)
    {
        // Store the new segmentation texture
        _segmentationTexture = segmentationTexture;
        _hasNewSegmentation = true;
        
        if (_debugMode)
            Debug.Log("ARWallPainter: Received new segmentation texture");
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Process added planes
        foreach (ARPlane plane in args.added)
        {
            // Check if it's a vertical plane (wall)
            if (IsVerticalPlane(plane))
            {
                // Create a copy of the wall material for this plane
                Material planeMaterial = new Material(_wallMaterial);
                
                // Apply to plane's mesh
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = planeMaterial;
                    
                    // Store in dictionary
                    _wallPlaneMaterials[plane.trackableId] = planeMaterial;
                    
                    if (_debugMode)
                        Debug.Log($"ARWallPainter: Applied wall material to plane {plane.trackableId}");
                }
            }
        }
        
        // Remove deleted planes from dictionary
        foreach (ARPlane plane in args.removed)
        {
            if (_wallPlaneMaterials.ContainsKey(plane.trackableId))
            {
                Material material = _wallPlaneMaterials[plane.trackableId];
                if (material != null)
                    Destroy(material);
                    
                _wallPlaneMaterials.Remove(plane.trackableId);
            }
        }
        
        // Force update wall planes if we have segmentation data
        if (_segmentationTexture != null)
        {
            UpdateWallPlanes();
        }
    }
    
    /// <summary>
    /// Update plane materials based on segmentation data - optimized version
    /// </summary>
    private void UpdateWallPlanes()
    {
        if (_segmentationTexture == null || _arCamera == null) return;
        
        // Clear the processed planes for this update
        _processedPlanes.Clear();
        
        // Get texture dimensions once
        int textureWidth = _segmentationTexture.width;
        int textureHeight = _segmentationTexture.height;
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        
        // Compute sample counts based on device performance
        // Less powerful devices use fewer sample points
        int sampleCount = _samplePointCount; // Default from serialized field
        if (SystemInfo.processorFrequency < 2000) // Less than 2GHz
            sampleCount = Mathf.Max(1, sampleCount - 1);
        
        // Process planes in batches for better performance
        int processedCount = 0;
        int maxPlanesPerFrame = 3; // Limit number of planes processed per frame
        
        // Loop through tracked AR planes
        foreach (ARPlane plane in _planeManager.trackables)
        {
            // Skip if already processed enough planes this frame
            if (processedCount >= maxPlanesPerFrame)
                break;
                
            // Optimization: Skip already known wall planes that haven't changed
            if (_knownWallPlanes.Contains(plane.trackableId) && plane.trackingState == TrackingState.Tracking)
            {
                _processedPlanes.Add(plane.trackableId);
                continue;
            }
            
            // Skip non-vertical planes
            if (!IsVerticalPlane(plane)) continue;
            
            // Mark as processed
            _processedPlanes.Add(plane.trackableId);
            processedCount++;
            
            // Get or create material for this plane
            Material planeMaterial;
            if (!_wallPlaneMaterials.TryGetValue(plane.trackableId, out planeMaterial))
            {
                planeMaterial = new Material(_wallMaterial);
                
                // Apply to plane's mesh
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = planeMaterial;
                    _wallPlaneMaterials[plane.trackableId] = planeMaterial;
                }
            }
            
            // Skip further processing if renderer or material is missing
            MeshRenderer planeRenderer = plane.GetComponent<MeshRenderer>();
            if (planeRenderer == null || planeMaterial == null) continue;
            
            // Check if this plane corresponds to a wall in the segmentation
            bool isWall = false;
            float wallConfidence = 0f;
            
            // Sample multiple points on the plane to check for wall pixels
            Vector3[] samplePoints = GetPlaneSamplePoints(plane, sampleCount);
            int wallPixelCount = 0;
            
            for (int i = 0; i < samplePoints.Length; i++)
            {
                // Project point to screen space
                Vector2 screenPos = _arCamera.WorldToScreenPoint(samplePoints[i]);
                
                // Skip points outside the screen
                if (screenPos.x < 0 || screenPos.x > screenWidth || 
                    screenPos.y < 0 || screenPos.y > screenHeight)
                    continue;
                
                // Convert to texture coordinates
                int x = Mathf.Clamp(Mathf.RoundToInt(screenPos.x * textureWidth / screenWidth), 0, textureWidth - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(screenPos.y * textureHeight / screenHeight), 0, textureHeight - 1);
                
                // Check pixel value
                Color pixel = _segmentationTexture.GetPixel(x, y);
                
                // Check if pixel classID matches wall
                if (Mathf.Abs(pixel.r - _normalizedClassId) < 0.1f || pixel.b > _wallConfidenceThreshold)
                {
                    wallPixelCount++;
                    wallConfidence = Mathf.Max(wallConfidence, pixel.g);
                }
            }
            
            // If more than half of sample points are wall pixels, consider this a wall
            isWall = wallPixelCount > samplePoints.Length / 2;
            
            // Update known walls collection
            if (isWall)
                _knownWallPlanes.Add(plane.trackableId);
            else
                _knownWallPlanes.Remove(plane.trackableId);
            
            // Update material visibility based on wall detection
            planeRenderer.enabled = isWall;
            
            // Update opacity based on confidence
            if (isWall)
            {
                Color color = planeMaterial.color;
                color.a = 0.4f + (wallConfidence * 0.6f); // Adjust opacity based on confidence
                planeMaterial.color = color;
                
                if (_debugMode)
                    Debug.Log($"ARWallPainter: Wall detected on plane {plane.trackableId} with confidence {wallConfidence:F2}");
            }
        }
        
        // Remove planes that weren't processed from known walls
        List<TrackableId> toRemove = new List<TrackableId>();
        foreach (TrackableId id in _knownWallPlanes)
        {
            if (!_processedPlanes.Contains(id))
                toRemove.Add(id);
        }
        
        foreach (TrackableId id in toRemove)
            _knownWallPlanes.Remove(id);
    }
    
    /// <summary>
    /// Check if a plane is vertical (wall)
    /// </summary>
    private bool IsVerticalPlane(ARPlane plane)
    {
        if (plane == null) return false;
        
        // Check alignment
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            return true;
        }
        
        // Additional check using normal
        Vector3 normal = plane.normal;
        float dotWithUp = Vector3.Dot(normal, Vector3.up);
        
        // If the normal is close to horizontal (perpendicular to up vector), it's a vertical plane
        return Mathf.Abs(dotWithUp) < 0.3f;
    }
    
    /// <summary>
    /// Get sample points distributed across a plane
    /// </summary>
    private Vector3[] GetPlaneSamplePoints(ARPlane plane, int sampleCount)
    {
        Vector3[] samplePoints = new Vector3[sampleCount];
        
        // Use plane center as first sample point
        samplePoints[0] = plane.center;
        
        // Get plane size
        Vector2 planeSize = plane.size;
        
        // Add points distributed across the plane
        for (int i = 1; i < sampleCount; i++)
        {
            float x = plane.center.x + (((i % 3) - 1) * planeSize.x * 0.3f);
            float y = plane.center.y + (((i / 3) % 3 - 1) * planeSize.y * 0.3f);
            float z = plane.center.z;
            
            // Ensure point is on the plane
            Vector3 pointOnPlane = new Vector3(x, y, z);
            Vector3 closestPointOnPlane = plane.center + Vector3.ProjectOnPlane(pointOnPlane - plane.center, plane.normal);
            
            samplePoints[i] = closestPointOnPlane;
        }
        
        return samplePoints;
    }
    
    /// <summary>
    /// Set the wall color for all wall planes
    /// </summary>
    public void SetWallColor(Color color)
    {
        // Update base material
        if (_wallMaterial != null)
        {
            Color newColor = color;
            newColor.a = _wallMaterial.color.a; // Keep original opacity
            _wallMaterial.color = newColor;
        }
        
        // Update all individual plane materials
        foreach (Material material in _wallPlaneMaterials.Values)
        {
            if (material != null)
            {
                Color newColor = color;
                newColor.a = material.color.a; // Keep individual plane opacity
                material.color = newColor;
            }
        }
        
        if (_debugMode)
            Debug.Log($"ARWallPainter: Updated wall color to {color}");
    }
    
    /// <summary>
    /// Set the opacity for all wall planes
    /// </summary>
    public void SetWallOpacity(float opacity)
    {
        // Clamp opacity
        opacity = Mathf.Clamp01(opacity);
        
        // Update base material
        if (_wallMaterial != null)
        {
            Color color = _wallMaterial.color;
            color.a = opacity;
            _wallMaterial.color = color;
        }
        
        // Update all individual plane materials
        foreach (Material material in _wallPlaneMaterials.Values)
        {
            if (material != null)
            {
                Color color = material.color;
                color.a = opacity;
                material.color = color;
            }
        }
        
        if (_debugMode)
            Debug.Log($"ARWallPainter: Updated wall opacity to {opacity}");
    }
} 