using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab;

/// <summary>
/// An AR-aware wall mesh renderer that integrates with ARWallAnchor to ensure walls
/// are properly anchored in the AR space rather than following the camera
/// </summary>
public class ARAwareWallMeshRenderer : MonoBehaviour
{
    [Header("AR References")]
    [SerializeField] private ARWallAnchor _arWallAnchor;
    [SerializeField] private ARCameraManager _arCameraManager;
    [SerializeField] private ARRaycastManager _arRaycastManager;
    [SerializeField] private ARPlaneManager _arPlaneManager;

    [Header("Segmentation")]
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    [SerializeField] private byte _wallClassId = 9; // ADE20K wall class ID
    [SerializeField] private float _wallConfidenceThreshold = 0.5f;

    [Header("Wall Rendering")]
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private float _wallThickness = 0.05f;
    [SerializeField] private bool _usePhysicsRaycast = true;
    [SerializeField] private LayerMask _raycastLayers = -1;
    [SerializeField] private float _maxRaycastDistance = 10f;

    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private bool _visualizeSegmentation = false;
    [SerializeField] private Color _debugColor = new Color(0.2f, 0.8f, 1.0f, 0.5f);

    // Public properties to access private fields
    public ARWallAnchor WallAnchor { 
        get { return _arWallAnchor; } 
        set { _arWallAnchor = value; } 
    }
    
    public ARCameraManager CameraManager { 
        get { return _arCameraManager; } 
        set { _arCameraManager = value; } 
    }
    
    public ARRaycastManager RaycastManager { 
        get { return _arRaycastManager; } 
        set { _arRaycastManager = value; } 
    }
    
    public ARPlaneManager PlaneManager { 
        get { return _arPlaneManager; } 
        set { _arPlaneManager = value; } 
    }
    
    public EnhancedDeepLabPredictor Predictor { 
        get { return _predictor; } 
        set { _predictor = value; } 
    }
    
    public Material WallMaterial { 
        get { return _wallMaterial; } 
        set { _wallMaterial = value; } 
    }

    // State tracking
    private bool _predictorInitialized = false;
    private Texture2D _segmentationTexture;
    private Camera _arCamera;
    private Transform _wallsContainer;
    private int _processedFrameCount = 0;
    private int _frameInterval = 10; // Only process every 10th frame for performance
    
    // Raycast results reused to avoid garbage collection
    private List<ARRaycastHit> _arHits = new List<ARRaycastHit>();

    private void Awake()
    {
        // Find references if not assigned
        if (_arWallAnchor == null)
            _arWallAnchor = FindObjectOfType<ARWallAnchor>();
            
        if (_arCameraManager == null)
            _arCameraManager = FindObjectOfType<ARCameraManager>();
            
        if (_arRaycastManager == null)
            _arRaycastManager = FindObjectOfType<ARRaycastManager>();
            
        if (_arPlaneManager == null)
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        
        // Get camera reference
        if (_arCameraManager != null)
            _arCamera = _arCameraManager.GetComponent<Camera>();
            
        // Create walls container
        _wallsContainer = new GameObject("Walls").transform;
        _wallsContainer.SetParent(transform);
        
        // Create default wall material if not assigned
        if (_wallMaterial == null)
        {
            _wallMaterial = new Material(Shader.Find("Standard"));
            _wallMaterial.color = _debugColor;
            
            if (_showDebugInfo)
                Debug.Log("ARAwareWallMeshRenderer: Created default wall material");
        }
    }

    private void OnEnable()
    {
        // Subscribe to segmentation events
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted += OnSegmentationCompleted;
            
            if (_showDebugInfo)
                Debug.Log("ARAwareWallMeshRenderer: Subscribed to segmentation events");
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from events
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted -= OnSegmentationCompleted;
        }
    }

    private void Update()
    {
        _processedFrameCount++;
        
        // Only process every Nth frame for performance
        if (_processedFrameCount % _frameInterval != 0)
            return;
            
        // Process segmentation if available
        if (_segmentationTexture != null && _predictorInitialized && _arCamera != null)
        {
            ProcessSegmentationToCreateWalls();
        }
    }

    private void OnSegmentationCompleted(Texture2D segmentationTexture)
    {
        _segmentationTexture = segmentationTexture;
        
        if (!_predictorInitialized)
        {
            _predictorInitialized = true;
            if (_showDebugInfo)
                Debug.Log("ARAwareWallMeshRenderer: Predictor initialized, segmentation available");
        }
        
        // Visualize segmentation for debugging
        if (_visualizeSegmentation)
        {
            ShowDebugTexture(segmentationTexture);
        }
    }

    private void ProcessSegmentationToCreateWalls()
    {
        int width = _segmentationTexture.width;
        int height = _segmentationTexture.height;
        
        // Compute normalized wall class ID
        float normalizedClassId = _wallClassId / 255f;
        
        // Sample grid points in the segmentation
        for (int y = 0; y < height; y += height / 20)
        {
            for (int x = 0; x < width; x += width / 20)
            {
                // Check if this pixel belongs to a wall
                Color pixel = _segmentationTexture.GetPixel(x, y);
                
                // Check pixel value - adjust this based on your segmentation output format
                bool isWall = Mathf.Abs(pixel.r - normalizedClassId) < 0.1f || pixel.g > _wallConfidenceThreshold;
                
                if (isWall)
                {
                    // Convert to screen coordinates
                    Vector2 screenPoint = new Vector2(
                        (float)x / width * Screen.width,
                        (float)y / height * Screen.height);
                        
                    // Try AR raycast first to find AR planes
                    if (_arRaycastManager != null && 
                        _arRaycastManager.Raycast(screenPoint, _arHits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
                    {
                        // We hit an AR plane
                        ARRaycastHit hit = _arHits[0];
                        
                        // Create a wall aligned with the plane
                        CreateWallMeshWithARAnchor(hit.pose.position, hit.pose.rotation, hit.trackableId);
                    }
                    else if (_usePhysicsRaycast)
                    {
                        // Fallback to physics raycast
                        Ray ray = _arCamera.ScreenPointToRay(screenPoint);
                        if (Physics.Raycast(ray, out RaycastHit hit, _maxRaycastDistance, _raycastLayers))
                        {
                            // Create a wall at hit point
                            Quaternion rot = Quaternion.LookRotation(-hit.normal);
                            CreateWallMeshAtPosition(hit.point, rot);
                        }
                        else
                        {
                            // If no hit, create wall at fixed distance from camera
                            Vector3 position = ray.GetPoint(2.0f);
                            Quaternion rotation = Quaternion.LookRotation(-ray.direction);
                            CreateWallMeshAtPosition(position, rotation);
                        }
                    }
                    
                    // Skip adjacent pixels to avoid creating too many walls in the same area
                    x += width / 10;
                }
            }
        }
    }

    /// <summary>
    /// Creates a wall mesh associated with an AR plane
    /// </summary>
    private void CreateWallMeshWithARAnchor(Vector3 position, Quaternion rotation, UnityEngine.XR.ARSubsystems.TrackableId planeId)
    {
        if (_arWallAnchor != null)
        {
            // First, create the wall mesh
            GameObject wallObj = CreateWallMeshAtPosition(position, rotation);
            
            // Now tell ARWallAnchor to anchor the plane
            _arWallAnchor.AnchorWallPlane(planeId);
            
            if (_showDebugInfo)
                Debug.Log($"ARAwareWallMeshRenderer: Created wall with anchor for plane {planeId}");
        }
    }

    /// <summary>
    /// Creates a wall mesh at the specified position and rotation
    /// </summary>
    private GameObject CreateWallMeshAtPosition(Vector3 position, Quaternion rotation)
    {
        // Find the closest AR plane to this position (optional)
        ARPlane closestPlane = FindClosestARPlane(position);
        
        // Create wall object
        GameObject wallObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wallObj.name = "Wall_" + _wallsContainer.childCount;
        
        // Set parent and transform
        wallObj.transform.SetParent(_wallsContainer);
        wallObj.transform.position = position;
        wallObj.transform.rotation = rotation;
        
        // Adjust scale based on distance or AR plane size
        float wallWidth = 1.0f;
        float wallHeight = 1.0f;
        
        if (closestPlane != null)
        {
            // Use AR plane dimensions
            wallWidth = closestPlane.size.x;
            wallHeight = closestPlane.size.y;
        }
        else
        {
            // Use distance-based size
            float distanceFromCamera = Vector3.Distance(_arCamera.transform.position, position);
            wallWidth = 0.5f * distanceFromCamera;
            wallHeight = 0.5f * distanceFromCamera;
        }
        
        wallObj.transform.localScale = new Vector3(wallWidth, wallHeight, 1f);
        
        // Apply wall material
        MeshRenderer renderer = wallObj.GetComponent<MeshRenderer>();
        if (renderer != null && _wallMaterial != null)
        {
            renderer.material = new Material(_wallMaterial);
        }
        
        if (_showDebugInfo)
            Debug.Log($"ARAwareWallMeshRenderer: Created wall at {position} with size {wallWidth}x{wallHeight}");
            
        return wallObj;
    }

    /// <summary>
    /// Finds the closest AR plane to the specified position
    /// </summary>
    private ARPlane FindClosestARPlane(Vector3 position)
    {
        if (_arPlaneManager == null)
            return null;
            
        ARPlane closestPlane = null;
        float closestDistance = float.MaxValue;
        
        foreach (ARPlane plane in _arPlaneManager.trackables)
        {
            if (IsVerticalPlane(plane))
            {
                float distance = Vector3.Distance(position, plane.center);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPlane = plane;
                }
            }
        }
        
        // Only return plane if it's reasonably close
        return (closestDistance < 1.0f) ? closestPlane : null;
    }

    /// <summary>
    /// Checks if a plane is vertical (potential wall)
    /// </summary>
    private bool IsVerticalPlane(ARPlane plane)
    {
        if (plane == null) return false;
        
        // Check alignment
        if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
            return true;
            
        // Additional check with normal vector
        Vector3 normal = plane.normal;
        float dotWithUp = Vector3.Dot(normal, Vector3.up);
        
        // If dot product with up is close to 0, the plane is vertical
        return Mathf.Abs(dotWithUp) < 0.3f;
    }

    /// <summary>
    /// Shows a debug visualization of the segmentation texture
    /// </summary>
    private void ShowDebugTexture(Texture2D texture)
    {
        // Find or create debug visualizer
        Transform visualizer = transform.Find("DebugVisualizer");
        if (visualizer == null)
        {
            GameObject visualizerObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visualizerObj.name = "DebugVisualizer";
            visualizerObj.transform.SetParent(transform);
            
            // Position in front of camera
            if (_arCamera != null)
            {
                visualizerObj.transform.position = _arCamera.transform.position + _arCamera.transform.forward * 0.3f;
                visualizerObj.transform.rotation = _arCamera.transform.rotation;
            }
            
            // Scale appropriately
            visualizerObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            
            // Create material
            Material mat = new Material(Shader.Find("Unlit/Texture"));
            visualizerObj.GetComponent<MeshRenderer>().material = mat;
            
            visualizer = visualizerObj.transform;
        }
        
        // Update position every frame to follow camera
        if (_arCamera != null)
        {
            visualizer.position = _arCamera.transform.position + _arCamera.transform.forward * 0.3f;
            visualizer.rotation = _arCamera.transform.rotation;
        }
        
        // Update texture
        visualizer.GetComponent<MeshRenderer>().material.mainTexture = texture;
    }

    /// <summary>
    /// Sets the color of all wall meshes
    /// </summary>
    public void SetWallColor(Color color)
    {
        if (_wallsContainer == null) return;
        
        // Update the base material
        if (_wallMaterial != null)
        {
            _wallMaterial.color = color;
        }
        
        // Update all existing walls
        for (int i = 0; i < _wallsContainer.childCount; i++)
        {
            MeshRenderer renderer = _wallsContainer.GetChild(i).GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = color;
            }
        }
        
        if (_showDebugInfo)
            Debug.Log($"ARAwareWallMeshRenderer: Set wall color to {color}");
    }

    /// <summary>
    /// Clear all wall meshes
    /// </summary>
    public void ClearWalls()
    {
        if (_wallsContainer == null) return;
        
        // Destroy all children
        for (int i = _wallsContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_wallsContainer.GetChild(i).gameObject);
        }
        
        if (_showDebugInfo)
            Debug.Log("ARAwareWallMeshRenderer: Cleared all walls");
    }

    /// <summary>
    /// Toggle visualization of segmentation
    /// </summary>
    public void ToggleSegmentationVisualization()
    {
        _visualizeSegmentation = !_visualizeSegmentation;
        
        // Hide the visualizer if turning off
        if (!_visualizeSegmentation)
        {
            Transform visualizer = transform.Find("DebugVisualizer");
            if (visualizer != null)
            {
                visualizer.gameObject.SetActive(false);
            }
        }
        else if (_segmentationTexture != null)
        {
            // Show the visualizer with current texture
            ShowDebugTexture(_segmentationTexture);
        }
        
        if (_showDebugInfo)
            Debug.Log($"ARAwareWallMeshRenderer: Segmentation visualization set to {_visualizeSegmentation}");
    }
} 