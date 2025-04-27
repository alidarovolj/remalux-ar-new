using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;

/// <summary>
/// Automatically configures and connects all components needed for AR Wall Detection
/// and proper anchoring of walls in the AR space
/// </summary>
public class ARWallDetectionSetup : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private bool _autoSetup = true;
    [SerializeField] private bool _replaceExistingSetup = false;
    
    [Header("AR Components")]
    [SerializeField] private ARSession _arSession;
    [SerializeField] private ARCameraManager _arCameraManager;
    [SerializeField] private ARPlaneManager _arPlaneManager;
    [SerializeField] private ARRaycastManager _arRaycastManager;
    
    [Header("Wall Detection")]
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private Color _wallColor = new Color(0.2f, 0.8f, 1.0f, 0.5f);
    
    // References to generated components
    private ARWallAnchor _wallAnchor;
    private ARAwareWallMeshRenderer _wallRenderer;
    private WallAnchorConnector _anchorConnector;
    
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
            
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
    }
    
    private void Start()
    {
        if (_autoSetup)
        {
            SetupAR();
        }
    }
    
    /// <summary>
    /// Configure all AR components for wall detection
    /// </summary>
    public void SetupAR()
    {
        // Check if we have all required components
        if (_arSession == null || _arCameraManager == null || 
            _arPlaneManager == null || _arRaycastManager == null)
        {
            Debug.LogError("ARWallDetectionSetup: Missing required AR components!");
            return;
        }
        
        // Set up AR plane detection to include vertical surfaces
        _arPlaneManager.requestedDetectionMode = 
            UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical | 
            UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
            
        Debug.Log("ARWallDetectionSetup: Configured AR Plane Manager to detect vertical surfaces");
        
        // Create components for wall anchoring if they don't exist or if replacing existing
        SetupWallAnchor();
        SetupWallRenderer();
        SetupAnchorConnector();
        
        Debug.Log("ARWallDetectionSetup: Setup complete!");
    }
    
    /// <summary>
    /// Set up the ARWallAnchor component
    /// </summary>
    private void SetupWallAnchor()
    {
        GameObject anchorObj = null;
        
        // Check if we already have an anchor manager
        _wallAnchor = FindObjectOfType<ARWallAnchor>();
        
        if (_wallAnchor != null && !_replaceExistingSetup)
        {
            Debug.Log("ARWallDetectionSetup: Using existing ARWallAnchor");
            anchorObj = _wallAnchor.gameObject;
        }
        else
        {
            // If replacing or none exists, create a new GameObject
            if (_wallAnchor != null && _replaceExistingSetup)
            {
                DestroyImmediate(_wallAnchor);
            }
            
            anchorObj = new GameObject("AR Wall Anchor System");
            
            // Add required components
            if (!anchorObj.TryGetComponent<ARAnchorManager>(out _))
            {
                anchorObj.AddComponent<ARAnchorManager>();
            }
            
            _wallAnchor = anchorObj.AddComponent<ARWallAnchor>();
            
            Debug.Log("ARWallDetectionSetup: Created ARWallAnchor component");
        }
    }
    
    /// <summary>
    /// Set up the ARAwareWallMeshRenderer component
    /// </summary>
    private void SetupWallRenderer()
    {
        GameObject rendererObj = null;
        
        // Check if we already have a renderer
        _wallRenderer = FindObjectOfType<ARAwareWallMeshRenderer>();
        
        if (_wallRenderer != null && !_replaceExistingSetup)
        {
            Debug.Log("ARWallDetectionSetup: Using existing ARAwareWallMeshRenderer");
            rendererObj = _wallRenderer.gameObject;
        }
        else
        {
            // If replacing or none exists, create a new GameObject
            if (_wallRenderer != null && _replaceExistingSetup)
            {
                DestroyImmediate(_wallRenderer);
            }
            
            rendererObj = new GameObject("AR Wall Renderer");
            _wallRenderer = rendererObj.AddComponent<ARAwareWallMeshRenderer>();
            
            // Configure renderer
            if (_wallMaterial != null)
            {
                Material newMaterial = new Material(_wallMaterial);
                newMaterial.color = _wallColor;
                _wallRenderer.WallMaterial = newMaterial;
            }
            
            Debug.Log("ARWallDetectionSetup: Created ARAwareWallMeshRenderer component");
        }
        
        // Set up references
        if (_wallRenderer != null)
        {
            _wallRenderer.WallAnchor = _wallAnchor;
            _wallRenderer.CameraManager = _arCameraManager;
            _wallRenderer.RaycastManager = _arRaycastManager;
            _wallRenderer.PlaneManager = _arPlaneManager;
            _wallRenderer.Predictor = _predictor;
        }
    }
    
    /// <summary>
    /// Set up the WallAnchorConnector component
    /// </summary>
    private void SetupAnchorConnector()
    {
        GameObject connectorObj = null;
        
        // Check if we already have a connector
        _anchorConnector = FindObjectOfType<WallAnchorConnector>();
        
        if (_anchorConnector != null && !_replaceExistingSetup)
        {
            Debug.Log("ARWallDetectionSetup: Using existing WallAnchorConnector");
            connectorObj = _anchorConnector.gameObject;
        }
        else
        {
            // If replacing or none exists, create a new GameObject
            if (_anchorConnector != null && _replaceExistingSetup)
            {
                DestroyImmediate(_anchorConnector);
            }
            
            connectorObj = new GameObject("AR Wall Anchor Connector");
            
            // Add required components
            if (!connectorObj.TryGetComponent<ARRaycastManager>(out _))
            {
                connectorObj.AddComponent<ARRaycastManager>();
            }
            
            // Add ARAnchorManager if needed
            if (!connectorObj.TryGetComponent<ARAnchorManager>(out _))
            {
                connectorObj.AddComponent<ARAnchorManager>();
            }
            
            _anchorConnector = connectorObj.AddComponent<WallAnchorConnector>();
            
            Debug.Log("ARWallDetectionSetup: Created WallAnchorConnector component");
        }
        
        // Set up references
        if (_anchorConnector != null)
        {
            _anchorConnector.CameraManager = _arCameraManager;
            _anchorConnector.Predictor = _predictor;
            _anchorConnector.WallAnchor = _wallAnchor;
            _anchorConnector.ARCamera = _arCameraManager?.GetComponent<Camera>();
            _anchorConnector.ARPlaneManager = _arPlaneManager;
            
            // Find and assign ARAnchorManager
            ARAnchorManager anchorManager = connectorObj.GetComponent<ARAnchorManager>();
            if (anchorManager == null)
            {
                anchorManager = FindObjectOfType<ARAnchorManager>();
            }
            
            if (anchorManager != null)
            {
                _anchorConnector.AnchorManager = anchorManager;
                Debug.Log("ARWallDetectionSetup: Connected WallAnchorConnector with ARAnchorManager");
            }
        }
    }
} 