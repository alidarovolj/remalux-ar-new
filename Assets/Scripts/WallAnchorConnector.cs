using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ML.DeepLab;
using System.Collections.Generic;

/// <summary>
/// Connects the wall segmentation from DeepLab/SegFormer with AR anchors
/// to ensure walls stay fixed in the real world rather than following the camera
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public class WallAnchorConnector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARCameraManager _arCameraManager;
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    [SerializeField] private ARWallAnchor _wallAnchor;
    [SerializeField] private Camera _arCamera;
    
    [Header("Settings")]
    [SerializeField] private int _samplesPerWall = 5;
    [SerializeField] private float _raycastDistance = 10f;
    [SerializeField] private LayerMask _raycastLayers = -1;
    [SerializeField] private bool _debugMode = true;
    [SerializeField] private float _updateInterval = 0.5f;
    
    // Public properties for external access
    public ARCameraManager CameraManager {
        get { return _arCameraManager; }
        set { _arCameraManager = value; }
    }
    
    public EnhancedDeepLabPredictor Predictor {
        get { return _predictor; }
        set { _predictor = value; }
    }
    
    public ARWallAnchor WallAnchor {
        get { return _wallAnchor; }
        set { _wallAnchor = value; }
    }
    
    public Camera ARCamera {
        get { return _arCamera; }
        set { _arCamera = value; }
    }
    
    // Components
    private ARRaycastManager _raycastManager;
    
    // State tracking
    private Texture2D _latestSegmentation;
    private bool _segmentationReady = false;
    private float _lastUpdateTime = 0f;
    
    // Raycast results reused to avoid garbage collection
    private readonly List<ARRaycastHit> _raycastHits = new List<ARRaycastHit>();
    
    private void Awake()
    {
        _raycastManager = GetComponent<ARRaycastManager>();
        
        // Get references if not assigned
        if (_arCameraManager == null)
            _arCameraManager = FindObjectOfType<ARCameraManager>();
        
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        
        if (_wallAnchor == null)
            _wallAnchor = FindObjectOfType<ARWallAnchor>();
            
        if (_arCamera == null && _arCameraManager != null)
            _arCamera = _arCameraManager.GetComponent<Camera>();
    }
    
    private void OnEnable()
    {
        // Subscribe to segmentation events
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted += OnSegmentationCompleted;
            
            if (_debugMode)
                Debug.Log("WallAnchorConnector: Subscribed to segmentation events");
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
        // Only process at the specified interval
        if (_segmentationReady && Time.time - _lastUpdateTime > _updateInterval)
        {
            ProcessSegmentationForAnchors();
            _lastUpdateTime = Time.time;
        }
    }
    
    /// <summary>
    /// Called when new segmentation data is available
    /// </summary>
    private void OnSegmentationCompleted(Texture2D segmentationTexture)
    {
        _latestSegmentation = segmentationTexture;
        _segmentationReady = true;
        
        if (_debugMode)
            Debug.Log("WallAnchorConnector: New segmentation received");
    }
    
    /// <summary>
    /// Process the latest segmentation to find wall areas and anchor them in AR space
    /// </summary>
    private void ProcessSegmentationForAnchors()
    {
        if (_latestSegmentation == null || _arCamera == null || _raycastManager == null)
            return;
            
        if (_debugMode)
            Debug.Log("WallAnchorConnector: Processing segmentation to create anchors");
            
        int width = _latestSegmentation.width;
        int height = _latestSegmentation.height;
        
        // Sample points from the segmentation to raycast for wall detection
        for (int y = 0; y < height; y += height / 10)
        {
            for (int x = 0; x < width; x += width / 10)
            {
                // Check if this pixel is part of a wall in the segmentation
                Color pixel = _latestSegmentation.GetPixel(x, y);
                
                // Use red channel for class ID (assuming DeepLab/SegFormer output format)
                // Fix - explicit cast to byte
                byte wallClassId = (byte)_predictor.WallClassId;
                float normalizedClassId = wallClassId / 255f;
                
                // If the pixel represents a wall
                if (Mathf.Abs(pixel.r - normalizedClassId) < 0.1f || pixel.g > _predictor.ClassificationThreshold)
                {
                    // Convert to screen position
                    Vector2 screenPos = new Vector2(
                        (float)x / width * Screen.width,
                        (float)y / height * Screen.height);
                        
                    // Raycast from the camera through this pixel into AR space
                    Ray ray = _arCamera.ScreenPointToRay(screenPos);
                    
                    // Try AR raycast to find planes
                    if (_raycastManager.Raycast(screenPos, _raycastHits, TrackableType.PlaneWithinPolygon))
                    {
                        // Found a plane - use the first hit
                        ARRaycastHit hit = _raycastHits[0];
                        
                        // Get the hit plane
                        TrackableId planeId = hit.trackableId;
                        
                        // Tell the Wall Anchor system to anchor this plane
                        if (_wallAnchor != null)
                        {
                            _wallAnchor.AnchorWallPlane(planeId);
                            
                            if (_debugMode)
                                Debug.Log($"WallAnchorConnector: Anchored wall at screen position {screenPos} to plane {planeId}");
                                
                            // Skip to next region to avoid too many anchors in the same area
                            break;
                        }
                    }
                    else
                    {
                        // Fallback to physics raycast if AR raycast failed
                        if (Physics.Raycast(ray, out RaycastHit physicsHit, _raycastDistance, _raycastLayers))
                        {
                            // Create an anchor at this world position
                            GameObject anchorObj = new GameObject("Wall Anchor");
                            anchorObj.transform.position = physicsHit.point;
                            // Orient the anchor to face the normal of the hit surface
                            anchorObj.transform.rotation = Quaternion.LookRotation(-physicsHit.normal);
                            
                            // Add AR anchor component
                            ARAnchor anchor = anchorObj.AddComponent<ARAnchor>();
                            
                            if (_debugMode)
                                Debug.Log($"WallAnchorConnector: Created standalone anchor at {physicsHit.point}");
                        }
                    }
                }
            }
        }
    }
} 