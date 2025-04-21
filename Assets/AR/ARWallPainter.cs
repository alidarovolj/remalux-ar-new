using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

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
    [SerializeField] private float _planeUpdateInterval = 0.2f;
    [SerializeField] private bool _debugMode = true;
    
    // AR components
    private ARPlaneManager _planeManager;
    private Dictionary<TrackableId, Material> _wallPlaneMaterials = new Dictionary<TrackableId, Material>();
    
    // Segmentation data
    private Texture2D _segmentationTexture;
    private bool _hasNewSegmentation = false;
    private float _lastPlaneUpdateTime = 0f;
    
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
    }
    
    private void OnEnable()
    {
        // Subscribe to events
        if (_planeManager != null)
        {
            _planeManager.planesChanged += OnPlanesChanged;
            
            if (_debugMode)
                Debug.Log("ARWallPainter: Subscribed to planesChanged event");
        }
        
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted += OnSegmentationCompleted;
            
            if (_debugMode)
                Debug.Log("ARWallPainter: Subscribed to OnSegmentationCompleted event");
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from events
        if (_planeManager != null)
            _planeManager.planesChanged -= OnPlanesChanged;
            
        if (_predictor != null)
            _predictor.OnSegmentationCompleted -= OnSegmentationCompleted;
    }
    
    private void Update()
    {
        // Check if it's time to update plane materials
        if (_hasNewSegmentation && Time.time - _lastPlaneUpdateTime > _planeUpdateInterval)
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
    /// Update plane materials based on segmentation data
    /// </summary>
    private void UpdateWallPlanes()
    {
        if (_segmentationTexture == null || _cameraManager == null) return;
        
        Camera arCamera = _cameraManager.GetComponent<Camera>();
        if (arCamera == null) return;
        
        // Process all tracked planes
        foreach (ARPlane plane in _planeManager.trackables)
        {
            // Only process vertical planes
            if (!IsVerticalPlane(plane)) continue;
            
            // Check if this plane corresponds to a wall in the segmentation
            bool isWall = false;
            float wallConfidence = 0f;
            
            // Sample multiple points on the plane to check for wall pixels
            Vector3[] samplePoints = GetPlaneSamplePoints(plane, 5);
            int wallPixelCount = 0;
            
            foreach (Vector3 point in samplePoints)
            {
                // Project point to screen space
                Vector2 screenPos = arCamera.WorldToScreenPoint(point);
                
                // Convert to texture coordinates
                int x = Mathf.Clamp(Mathf.RoundToInt(screenPos.x * _segmentationTexture.width / Screen.width), 0, _segmentationTexture.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(screenPos.y * _segmentationTexture.height / Screen.height), 0, _segmentationTexture.height - 1);
                
                // Check pixel value
                Color pixel = _segmentationTexture.GetPixel(x, y);
                
                // Check if pixel classID matches wall
                float normalizedClassId = _wallClassId / 255.0f;
                if (Mathf.Abs(pixel.r - normalizedClassId) < 0.1f || pixel.b > _wallConfidenceThreshold)
                {
                    wallPixelCount++;
                    wallConfidence = Mathf.Max(wallConfidence, pixel.g);
                }
            }
            
            // If more than half of sample points are wall pixels, consider this a wall
            isWall = wallPixelCount > samplePoints.Length / 2;
            
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
            
            // Update material visibility based on wall detection
            if (planeMaterial != null)
            {
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    // Only show walls
                    renderer.enabled = isWall;
                    
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
            }
        }
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