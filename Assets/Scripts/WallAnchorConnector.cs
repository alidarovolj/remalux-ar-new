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
    [SerializeField] private ARAnchorManager _anchorManager;
    [SerializeField] private ARPlaneManager _arPlaneManager;
    
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
    
    public ARAnchorManager AnchorManager {
        get { return _anchorManager; }
        set { _anchorManager = value; }
    }
    
    public ARPlaneManager ARPlaneManager {
        get { return _arPlaneManager; }
        set { _arPlaneManager = value; }
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
        _raycastManager = GetComponent<ARRaycastManager>() 
                        ?? FindObjectOfType<ARRaycastManager>();
        _anchorManager = GetComponent<ARAnchorManager>() 
                        ?? FindObjectOfType<ARAnchorManager>();
        _arPlaneManager = GetComponent<ARPlaneManager>()
                        ?? FindObjectOfType<ARPlaneManager>();
        
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
        if (_latestSegmentation == null || _arCamera == null || _raycastManager == null || _anchorManager == null)
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
                        // берём первый результат и сразу точную позу
                        ARRaycastHit hit = _raycastHits[0];
                        Pose hitPose = hit.pose;
                        
                        // Получаем AR-плоскость, по которой произошло попадание
                        TrackableId planeId = hit.trackableId;
                        ARPlane hitPlane = null;
                        
                        // Находим плоскость среди отслеживаемых
                        if (_arPlaneManager != null) {
                            hitPlane = _arPlaneManager.GetPlane(planeId);
                        }
                        
                        // создаём якорь в этой позе используя плоскость или позу
                        ARAnchor anchor = null;
                        if (hitPlane != null) {
                            // Используем привязку к плоскости
                            anchor = _anchorManager.AttachAnchor(hitPlane, hitPose);
                        } else {
                            // Если не удалось найти плоскость, создаем обычный якорь
                            GameObject anchorGO = new GameObject("Wall Anchor");
                            anchorGO.transform.position = hitPose.position;
                            anchorGO.transform.rotation = hitPose.rotation;
                            anchor = anchorGO.AddComponent<ARAnchor>();
                        }
                        
                        if (anchor != null)
                        {
                            // «приклеиваем» ваш wall-представитель к якорю,
                            // сохраняя его текущие мировые координаты
                            var wallGO = _wallAnchor.gameObject;
                            wallGO.transform.SetParent(anchor.transform, worldPositionStays: true);
                            if (_debugMode) Debug.Log($"WallAnchorConnector: Anchored wall at {hitPose.position}");
                            break;
                        }
                    }
                    else
                    {
                        // Fallback to physics raycast if AR raycast failed
                        if (Physics.Raycast(ray, out RaycastHit physicsHit, _raycastDistance, _raycastLayers))
                        {
                            // Create an anchor at this world position using gameObject + component
                            Pose hitPose = new Pose(physicsHit.point, Quaternion.LookRotation(-physicsHit.normal));
                            
                            GameObject anchorGO = new GameObject("Wall Anchor (Physics)");
                            anchorGO.transform.position = hitPose.position;
                            anchorGO.transform.rotation = hitPose.rotation;
                            
                            ARAnchor anchor = anchorGO.AddComponent<ARAnchor>();
                            
                            if (anchor != null && _wallAnchor != null)
                            {
                                // Attach wall to the anchor
                                var wallGO = _wallAnchor.gameObject;
                                wallGO.transform.SetParent(anchor.transform, worldPositionStays: true);
                                
                                if (_debugMode)
                                    Debug.Log($"WallAnchorConnector: Created anchor using physics raycast at {hitPose.position}");
                            }
                        }
                    }
                }
            }
        }
    }
} 