using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ML.DeepLab;
using ML;
using UnityEngine.Events;

/// <summary>
/// Enhanced implementation of wall detection system that extends functionality
/// of the original RemaluxARWallSetup class with improved machine learning integration
/// </summary>
public class RemaluxWallDetectionSystem : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARSession _arSession;
    [SerializeField] private ARCameraManager _arCameraManager;
    [SerializeField] private ARPlaneManager _arPlaneManager;
    [SerializeField] private ARRaycastManager _arRaycastManager;
    
    [Header("Wall Configuration")]
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private float _minWallHeight = 2.4f;
    [SerializeField] private GameObject _wallAnchorPrefab;
    [SerializeField] private Color _wallColor = new Color(0.2f, 0.8f, 1.0f, 0.7f);
    
    [Header("Machine Learning")]
    [SerializeField] private DeepLabPredictor _deepLabPredictor;
    [SerializeField] private WallAnchorConnector _wallAnchorConnector;
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    
    [Header("Settings")]
    [SerializeField] private bool _autoDetectWalls = true;
    [SerializeField] private bool _debugMode = true;
    [SerializeField] private float _wallDetectionInterval = 0.5f;
    [SerializeField] private bool _autoSetup = true;
    
    // References to legacy setup if needed
    [SerializeField] private RemaluxARWallSetup _legacySetup;
    
    // Add a field for our segmentation event
    [Header("Events")]
    [SerializeField] private UnityEvent _onSegmentationComplete = new UnityEvent();
    
    // Property to access the segmentation event
    public UnityEvent OnSegmentationComplete => _onSegmentationComplete;
    
    private bool _isInitialized = false;
    private bool _isTracking = false;
    private float _lastDetectionTime = 0f;
    private List<ARWallAnchor> _wallAnchors = new List<ARWallAnchor>();
    
    private void Awake()
    {
        // Find required components if not assigned
        if (_arSession == null)
            _arSession = FindObjectOfType<ARSession>();
            
        if (_arCameraManager == null)
            _arCameraManager = FindObjectOfType<ARCameraManager>();
            
        if (_arPlaneManager == null)
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            
        if (_arRaycastManager == null)
            _arRaycastManager = FindObjectOfType<ARRaycastManager>();
            
        if (_deepLabPredictor == null)
            _deepLabPredictor = FindObjectOfType<DeepLabPredictor>();
            
        if (_wallAnchorConnector == null)
            _wallAnchorConnector = FindObjectOfType<WallAnchorConnector>();
    }
    
    private void Start()
    {
        // If we have a legacy setup, disable it to avoid conflicts
        if (_legacySetup != null && _legacySetup != this)
        {
            Debug.Log("RemaluxWallDetectionSystem: Found legacy setup. New system will take over.");
            _legacySetup.enabled = false;
        }
        
        // Validate required components
        _isInitialized = ValidateComponents();
        
        if (_isInitialized)
        {
            if (_debugMode)
                Debug.Log("RemaluxWallDetectionSystem: Successfully initialized components");
                
            // Subscribe to AR plane events
            SubscribeToAREvents();
            
            // Start wall detection if auto-detect is enabled
            if (_autoDetectWalls)
                StartWallDetection();
        }
        else
        {
            Debug.LogError("RemaluxWallDetectionSystem: Failed to initialize all required components");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        UnsubscribeFromAREvents();
    }
    
    /// <summary>
    /// Validates that all required components are present
    /// </summary>
    private bool ValidateComponents()
    {
        bool allValid = true;
        
        if (_arSession == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARSession component");
            allValid = false;
        }
        
        if (_arCameraManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARCameraManager component");
            allValid = false;
        }
        
        if (_arPlaneManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARPlaneManager component");
            allValid = false;
        }
        
        if (_arRaycastManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARRaycastManager component");
            allValid = false;
        }
        
        if (_deepLabPredictor == null)
        {
            Debug.LogWarning("RemaluxWallDetectionSystem: DeepLabPredictor not found, wall detection will be limited");
        }
        
        if (_wallAnchorConnector == null)
        {
            Debug.LogWarning("RemaluxWallDetectionSystem: WallAnchorConnector not found, will create one");
            GameObject connectorObject = new GameObject("Wall Anchor Connector");
            _wallAnchorConnector = connectorObject.AddComponent<WallAnchorConnector>();
        }
        
        if (_wallMaterial == null)
        {
            _wallMaterial = Resources.Load<Material>("Materials/WallMaterial");
            if (_wallMaterial == null)
            {
                Debug.LogWarning("RemaluxWallDetectionSystem: No wall material found, creating default");
                _wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _wallMaterial.color = new Color(0.9f, 0.9f, 0.9f, 0.85f);
            }
        }
        
        if (_wallAnchorPrefab == null)
        {
            Debug.LogWarning("RemaluxWallDetectionSystem: Wall anchor prefab not set, will create one dynamically");
            
            // Create a basic wall anchor prefab
            _wallAnchorPrefab = new GameObject("Wall Anchor Template");
            _wallAnchorPrefab.AddComponent<ARWallAnchor>();
            _wallAnchorPrefab.AddComponent<ARAnchor>();
            
            // Hide the template
            _wallAnchorPrefab.SetActive(false);
        }
        
        return allValid;
    }
    
    /// <summary>
    /// Subscribe to all AR events needed for wall detection
    /// </summary>
    private void SubscribeToAREvents()
    {
        if (_arPlaneManager != null)
        {
            _arPlaneManager.planesChanged += OnPlanesChanged;
            
            if (_debugMode)
                Debug.Log("RemaluxWallDetectionSystem: Subscribed to AR plane events");
        }
        
        // Check for MLManager-based predictor
        MLManager mlManager = FindObjectOfType<MLManager>();
        if (mlManager != null)
        {
            // Use += for delegate events (Action<RenderTexture>)
            mlManager.OnSegmentationComplete += OnSegmentationCompleteTexture;
            
            if (_debugMode)
                Debug.Log("RemaluxWallDetectionSystem: Subscribed to MLManager segmentation events");
        }
        // If we have an EnhancedDeepLabPredictor, use its event
        else if (_predictor != null)
        {
            // Should already be a delegate (C# event)
            _predictor.OnSegmentationCompleted += OnSegmentationCompleteTexture;
            
            if (_debugMode)
                Debug.Log("RemaluxWallDetectionSystem: Subscribed to EnhancedDeepLabPredictor segmentation events");
        }
        // If we have a regular DeepLabPredictor, use our own event
        else if (_deepLabPredictor != null)
        {
            // Check if there's an adapter component
            var adapter = _deepLabPredictor.GetComponent<DeepLabPredictorEventAdapter>();
            if (adapter != null)
            {
                adapter.OnSegmentationComplete.AddListener(OnSegmentationCompleteHandler);
                
                if (_debugMode)
                    Debug.Log("RemaluxWallDetectionSystem: Subscribed to DeepLabPredictor events via adapter");
            }
            else
            {
                // Add a listener to our own event
                _onSegmentationComplete.AddListener(OnSegmentationCompleteHandler);
                
                if (_debugMode)
                    Debug.Log("RemaluxWallDetectionSystem: Subscribed to internal segmentation events");
            }
        }
    }
    
    /// <summary>
    /// Unsubscribe from all AR events
    /// </summary>
    private void UnsubscribeFromAREvents()
    {
        if (_arPlaneManager != null)
        {
            _arPlaneManager.planesChanged -= OnPlanesChanged;
        }
        
        // Check for MLManager-based predictor
        MLManager mlManager = FindObjectOfType<MLManager>();
        if (mlManager != null)
        {
            // Use -= for delegate events (Action<RenderTexture>)
            mlManager.OnSegmentationComplete -= OnSegmentationCompleteTexture;
        }
        // If we have an EnhancedDeepLabPredictor, use its event
        else if (_predictor != null)
        {
            // Should already be a delegate (C# event)
            _predictor.OnSegmentationCompleted -= OnSegmentationCompleteTexture;
        }
        // If we have a regular DeepLabPredictor, use our own event
        else if (_deepLabPredictor != null)
        {
            // Check if there's an adapter component
            var adapter = _deepLabPredictor.GetComponent<DeepLabPredictorEventAdapter>();
            if (adapter != null)
            {
                adapter.OnSegmentationComplete.RemoveListener(OnSegmentationCompleteHandler);
            }
            else
            {
                // Remove the listener from our own event
                _onSegmentationComplete.RemoveListener(OnSegmentationCompleteHandler);
            }
        }
    }
    
    /// <summary>
    /// Handler for segmentation completion with RenderTexture
    /// </summary>
    private void OnSegmentationCompleteTexture(RenderTexture segmentationTexture)
    {
        // This processes the segmentation result as a RenderTexture (new API)
        OnSegmentationCompleteHandler();
    }
    
    /// <summary>
    /// Handler for segmentation completion with Texture2D
    /// </summary>
    private void OnSegmentationCompleteTexture(Texture2D segmentationTexture)
    {
        // This processes the segmentation result as a Texture2D (new API)
        OnSegmentationCompleteHandler();
    }
    
    /// <summary>
    /// Process segmentation completion
    /// </summary>
    private void OnSegmentationCompleteHandler()
    {
        if (!_isTracking) return;
        
        // Forward the segmentation event to the wall anchor connector
        if (_wallAnchorConnector != null)
        {
            _wallAnchorConnector.ProcessSegmentationForAnchors();
        }
    }
    
    /// <summary>
    /// Start wall detection process
    /// </summary>
    public void StartWallDetection()
    {
        if (!_isInitialized)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Cannot start wall detection, system not initialized");
            return;
        }
        
        _isTracking = true;
        _lastDetectionTime = Time.time;
        
        if (_debugMode)
            Debug.Log("RemaluxWallDetectionSystem: Wall detection started");
    }
    
    /// <summary>
    /// Stop wall detection process
    /// </summary>
    public void StopWallDetection()
    {
        _isTracking = false;
        
        if (_debugMode)
            Debug.Log("RemaluxWallDetectionSystem: Wall detection stopped");
    }
    
    /// <summary>
    /// Handle AR plane changes
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!_isTracking) return;
        
        // Check for new vertical planes
        foreach (ARPlane plane in args.added)
        {
            if (IsVerticalPlane(plane))
            {
                CreateWallAnchorForPlane(plane);
            }
        }
        
        // Check for updated planes
        foreach (ARPlane plane in args.updated)
        {
            if (IsVerticalPlane(plane))
            {
                // Update wall anchor if it exists, or create new one
                bool found = false;
                foreach (ARWallAnchor wallAnchor in _wallAnchors)
                {
                    if (wallAnchor != null && wallAnchor.GetComponent<ARAnchor>() != null)
                    {
                        ARAnchor anchor = wallAnchor.GetComponent<ARAnchor>();
                        if (anchor.trackableId == plane.trackableId)
                        {
                            // Found existing anchor, update it
                            found = true;
                            break;
                        }
                    }
                }
                
                if (!found)
                {
                    CreateWallAnchorForPlane(plane);
                }
            }
        }
        
        // Remove wall anchors for removed planes
        foreach (ARPlane plane in args.removed)
        {
            for (int i = _wallAnchors.Count - 1; i >= 0; i--)
            {
                ARWallAnchor wallAnchor = _wallAnchors[i];
                if (wallAnchor != null && wallAnchor.GetComponent<ARAnchor>() != null)
                {
                    ARAnchor anchor = wallAnchor.GetComponent<ARAnchor>();
                    if (anchor.trackableId == plane.trackableId)
                    {
                        // Remove wall anchor
                        _wallAnchors.RemoveAt(i);
                        Destroy(wallAnchor.gameObject);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Create a wall anchor for an AR plane
    /// </summary>
    private void CreateWallAnchorForPlane(ARPlane plane)
    {
        // Create a new wall anchor for the plane
        GameObject wallAnchorObject = Instantiate(_wallAnchorPrefab, plane.transform.position, plane.transform.rotation);
        wallAnchorObject.name = $"Wall Anchor ({plane.trackableId})";
        wallAnchorObject.SetActive(true);
        
        // Get or add required components
        ARWallAnchor wallAnchor = wallAnchorObject.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
            wallAnchor = wallAnchorObject.AddComponent<ARWallAnchor>();
            
        ARAnchor anchor = wallAnchorObject.GetComponent<ARAnchor>();
        if (anchor == null)
            anchor = wallAnchorObject.AddComponent<ARAnchor>();
        
        // Add the wall anchor to the list
        _wallAnchors.Add(wallAnchor);
        
        if (_debugMode)
            Debug.Log($"RemaluxWallDetectionSystem: Created wall anchor for plane {plane.trackableId}");
    }
    
    /// <summary>
    /// Check if a plane is vertical (potential wall)
    /// </summary>
    private bool IsVerticalPlane(ARPlane plane)
    {
        // Check plane classification
        if (plane.classification == PlaneClassification.Wall)
            return true;
            
        // Check plane alignment
        if (plane.alignment == PlaneAlignment.Vertical)
            return true;
            
        // Check normal vector to see if it's approximately horizontal (indicating a vertical plane)
        Vector3 normal = plane.normal;
        float dotProduct = Vector3.Dot(normal, Vector3.up);
        float threshold = 0.3f;  // Adjust this threshold if needed
        
        return Mathf.Abs(dotProduct) < threshold;
    }
    
    /// <summary>
    /// Update loop for wall detection
    /// </summary>
    private void Update()
    {
        if (!_isTracking) return;
        
        // Check if it's time for the next wall detection
        if (Time.time - _lastDetectionTime > _wallDetectionInterval)
        {
            _lastDetectionTime = Time.time;
            
            // Trigger wall detection
            if (_wallAnchorConnector != null)
            {
                _wallAnchorConnector.ProcessSegmentationForAnchors();
            }
        }
    }
    
    /// <summary>
    /// Clear all detected walls
    /// </summary>
    public void ClearAllWalls()
    {
        // Remove all wall anchors
        foreach (ARWallAnchor wallAnchor in _wallAnchors)
        {
            if (wallAnchor != null)
            {
                Destroy(wallAnchor.gameObject);
            }
        }
        
        _wallAnchors.Clear();
        
        if (_debugMode)
            Debug.Log("RemaluxWallDetectionSystem: Cleared all wall anchors");
    }
    
    /// <summary>
    /// Get all detected wall anchors
    /// </summary>
    public List<ARWallAnchor> GetWallAnchors()
    {
        return _wallAnchors;
    }
} 