using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ML.DeepLab;
using UnityEngine.Events;

/// <summary>
/// Класс для настройки и управления AR стенами в приложении Remalux
/// </summary>
public class RemaluxARWallSetup : MonoBehaviour
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
    
    [Header("Events")]
    [SerializeField] private UnityEvent _onSegmentationComplete = new UnityEvent();
    
    private bool _isInitialized = false;
    private bool _isTracking = false;
    private float _lastDetectionTime = 0f;
    private List<ARWallAnchor> _wallAnchors = new List<ARWallAnchor>();
    
    public UnityEvent OnSegmentationComplete => _onSegmentationComplete;
    
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
            
        // Validate components in Awake instead of Start
        _isInitialized = ValidateComponents();
    }
    
    private void Start()
    {
        // Components are already validated in Awake
        
        if (_isInitialized)
        {
            if (_debugMode)
                Debug.Log("RemaluxARWallSetup: Successfully initialized components");
                
            // Subscribe to AR plane events
            SubscribeToAREvents();
            
            // Start wall detection if auto-detect is enabled
            if (_autoDetectWalls)
                StartWallDetection();
        }
        else
        {
            Debug.LogError("RemaluxARWallSetup: Failed to initialize all required components");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        UnsubscribeFromAREvents();
    }
    
    /// <summary>
    /// Проверяет, что все необходимые компоненты найдены
    /// </summary>
    private bool ValidateComponents()
    {
        bool allValid = true;
        
        if (_arSession == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARSession component");
            allValid = false;
        }
        
        if (_arCameraManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARCameraManager component");
            allValid = false;
        }
        
        if (_arPlaneManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARPlaneManager component");
            allValid = false;
        }
        
        if (_arRaycastManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARRaycastManager component");
            allValid = false;
        }
        
        if (_deepLabPredictor == null)
        {
            Debug.LogWarning("RemaluxARWallSetup: DeepLabPredictor not found, wall detection will be limited");
        }
        
        if (_wallAnchorConnector == null)
        {
            Debug.LogWarning("RemaluxARWallSetup: WallAnchorConnector not found, will create one");
            GameObject connectorObject = new GameObject("Wall Anchor Connector");
            _wallAnchorConnector = connectorObject.AddComponent<WallAnchorConnector>();
        }
        
        if (_wallMaterial == null)
        {
            _wallMaterial = Resources.Load<Material>("Materials/WallMaterial");
            if (_wallMaterial == null)
            {
                Debug.LogWarning("RemaluxARWallSetup: No wall material found, creating default");
                _wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _wallMaterial.color = new Color(0.9f, 0.9f, 0.9f, 0.85f);
            }
        }
        
        if (_wallAnchorPrefab == null)
        {
            Debug.LogWarning("RemaluxARWallSetup: Wall anchor prefab not set, will create one dynamically");
            
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
    /// Подписывается на события AR для отслеживания плоскостей
    /// </summary>
    private void SubscribeToAREvents()
    {
        if (_arPlaneManager != null)
        {
            _arPlaneManager.planesChanged += OnPlanesChanged;
            
            if (_debugMode)
                Debug.Log("RemaluxARWallSetup: Subscribed to AR plane events");
        }
        
        if (_deepLabPredictor != null && _onSegmentationComplete != null)
        {
            // Get the adapter or extension event
            var adapter = _deepLabPredictor.GetComponent<DeepLabPredictorEventAdapter>();
            if (adapter != null)
            {
                adapter.OnSegmentationComplete.AddListener(OnSegmentationCompleteHandler);
            }
            else
            {
                // Use our own event
                _onSegmentationComplete.AddListener(OnSegmentationCompleteHandler);
            }
            
            if (_debugMode)
                Debug.Log("RemaluxARWallSetup: Subscribed to segmentation events");
        }
    }
    
    /// <summary>
    /// Отписывается от событий AR
    /// </summary>
    private void UnsubscribeFromAREvents()
    {
        if (_arPlaneManager != null)
        {
            _arPlaneManager.planesChanged -= OnPlanesChanged;
        }
        
        if (_deepLabPredictor != null && _onSegmentationComplete != null)
        {
            // Get the adapter or extension event
            var adapter = _deepLabPredictor.GetComponent<DeepLabPredictorEventAdapter>();
            if (adapter != null)
            {
                adapter.OnSegmentationComplete.RemoveListener(OnSegmentationCompleteHandler);
            }
            else
            {
                // Use our own event
                _onSegmentationComplete.RemoveListener(OnSegmentationCompleteHandler);
            }
        }
    }
    
    /// <summary>
    /// Запускает обнаружение стен
    /// </summary>
    public void StartWallDetection()
    {
        if (!_isInitialized)
        {
            Debug.LogError("RemaluxARWallSetup: Cannot start wall detection, system not initialized");
            return;
        }
        
        _isTracking = true;
        _lastDetectionTime = Time.time;
        
        if (_debugMode)
            Debug.Log("RemaluxARWallSetup: Wall detection started");
    }
    
    /// <summary>
    /// Останавливает обнаружение стен
    /// </summary>
    public void StopWallDetection()
    {
        _isTracking = false;
        
        if (_debugMode)
            Debug.Log("RemaluxARWallSetup: Wall detection stopped");
    }
    
    /// <summary>
    /// Обрабатывает изменения в AR плоскостях
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!_isTracking) return;
        
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
        
        // NOTE: We no longer automatically create wall anchors for every vertical plane
        // Wall anchors will be created only after successful segmentation
    }
    
    /// <summary>
    /// Реагирует на завершение сегментации изображения
    /// </summary>
    private void OnSegmentationCompleteHandler()
    {
        if (!_isTracking) return;
        
        // Forward the segmentation event to the wall anchor connector
        if (_wallAnchorConnector != null)
        {
            _wallAnchorConnector.ProcessSegmentationForAnchors();
            
            // After processing segmentation, create wall anchors based on detected wall regions
            CreateWallAnchorsFromSegmentation();
        }
    }
    
    /// <summary>
    /// Создает привязки стен на основе сегментации
    /// </summary>
    private void CreateWallAnchorsFromSegmentation()
    {
        if (_arRaycastManager == null)
            return;
        
        // Get segmentation data - EnhancedDeepLabPredictor exposes GetSegmentationTexture()
        // while regular DeepLabPredictor uses PredictSegmentation()
        Texture2D segmentationTexture = null;
        
        if (_predictor != null)
        {
            // Try to get the texture from EnhancedDeepLabPredictor
            // EnhancedDeepLabPredictor has a GetSegmentationTexture method
            try {
                // Use reflection to access the method since it might not be directly accessible
                var method = _predictor.GetType().GetMethod("GetSegmentationTexture");
                if (method != null)
                {
                    segmentationTexture = method.Invoke(_predictor, null) as Texture2D;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"RemaluxARWallSetup: Error getting segmentation texture: {e.Message}");
            }
        }
        else if (_deepLabPredictor != null)
        {
            // For standard DeepLabPredictor, we need to use PredictSegmentation
            // Create a temporary texture from the camera
            Texture2D tempInput = new Texture2D(Screen.width, Screen.height);
            tempInput.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tempInput.Apply();
            
            // Use PredictSegmentation to get a RenderTexture
            RenderTexture renderTexture = _deepLabPredictor.PredictSegmentation(tempInput);
            
            // Convert RenderTexture to Texture2D
            if (renderTexture != null)
            {
                segmentationTexture = ConvertRenderTextureToTexture2D(renderTexture);
                Destroy(tempInput); // Clean up temporary texture
            }
        }
        
        if (segmentationTexture == null)
        {
            if (_debugMode)
                Debug.Log("RemaluxARWallSetup: No segmentation texture available");
            return;
        }
        
        // Check that we have all required components
        if (_arPlaneManager == null || _arRaycastManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing AR components required for wall anchor creation");
            return;
        }
        
        // Find all vertical planes to consider
        List<ARPlane> verticalPlanes = new List<ARPlane>();
        foreach (ARPlane plane in _arPlaneManager.trackables)
        {
            if (IsVerticalPlane(plane))
            {
                verticalPlanes.Add(plane);
            }
        }
        
        if (verticalPlanes.Count == 0)
        {
            if (_debugMode)
                Debug.Log("RemaluxARWallSetup: No vertical planes found for wall anchoring");
            return;
        }
        
        // Sample points from the screen where walls might be located
        int sampleCount = 5; // Number of samples across screen width and height
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        
        for (int x = 0; x < sampleCount; x++)
        {
            for (int y = 0; y < sampleCount; y++)
            {
                // Calculate screen position
                Vector2 screenPos = new Vector2(
                    Screen.width * ((float)x / (sampleCount - 1)),
                    Screen.height * ((float)y / (sampleCount - 1))
                );
                
                // Check if this point contains wall segmentation
                bool isWallSegment = IsWallSegmentAtScreenPos(screenPos, segmentationTexture);
                
                if (isWallSegment && _arRaycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
                {
                    var hit = hits[0];
                    
                    // Check if this plane is vertical
                    ARPlane hitPlane = _arPlaneManager.GetPlane(hit.trackableId);
                    if (hitPlane != null && IsVerticalPlane(hitPlane))
                    {
                        // Check if we already have an anchor for this plane
                        bool anchorExists = false;
                        foreach (ARWallAnchor wallAnchor in _wallAnchors)
                        {
                            if (wallAnchor != null && wallAnchor.ARPlane != null && 
                                wallAnchor.ARPlane.trackableId == hitPlane.trackableId)
                            {
                                anchorExists = true;
                                break;
                            }
                        }
                        
                        if (!anchorExists)
                        {
                            // Create a wall anchor at this position
                            CreateWallAnchorForPlane(hitPlane);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Проверяет, присутствует ли сегментация стены в указанной точке экрана
    /// </summary>
    private bool IsWallSegmentAtScreenPos(Vector2 screenPos, Texture2D segmentationTexture)
    {
        if (segmentationTexture == null)
            return false;
        
        // Find proper wall class ID - make sure to use the correct ID for your model!
        int wallClassId = 9; // Default for many models, but check documentation for your specific model
        
        if (_predictor != null)
        {
            // Use the predictor's wall class ID
            wallClassId = _predictor.WallClassId;
        }
        else if (_deepLabPredictor != null)
        {
            // Use the DeepLabPredictor's wall class ID
            wallClassId = _deepLabPredictor.WallClassId;
        }
        
        try
        {
            // Convert screen position to texture coordinates
            int textureX = Mathf.FloorToInt((screenPos.x / Screen.width) * segmentationTexture.width);
            int textureY = Mathf.FloorToInt((screenPos.y / Screen.height) * segmentationTexture.height);
            
            // Ensure we're within bounds
            textureX = Mathf.Clamp(textureX, 0, segmentationTexture.width - 1);
            textureY = Mathf.Clamp(textureY, 0, segmentationTexture.height - 1);
            
            // Get the pixel at this position
            Color pixel = segmentationTexture.GetPixel(textureX, textureY);
            int classId = Mathf.RoundToInt(pixel.r * 255);
            
            return classId == wallClassId;
        }
        catch (System.Exception e)
        {
            // Texture might not be readable
            Debug.LogWarning($"RemaluxARWallSetup: Could not read segmentation texture: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Converts a RenderTexture to a Texture2D
    /// </summary>
    private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
    {
        if (renderTexture == null)
            return null;
        
        // Create a new Texture2D with the dimensions of the RenderTexture
        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        
        // Read pixels from the RenderTexture
        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();
        RenderTexture.active = null;
        
        return texture2D;
    }
    
    /// <summary>
    /// Создает привязку стены для плоскости AR
    /// </summary>
    private void CreateWallAnchorForPlane(ARPlane plane)
    {
        // если вдруг шаблон не создан — создаём на лету
        if (_wallAnchorPrefab == null)
            ValidateComponents();

        if (_wallAnchorPrefab == null)
        {
            Debug.LogError("RemaluxARWallSetup: _wallAnchorPrefab всё ещё не задан — пропускаем создание якоря");
            return;
        }

        // Create a new wall anchor as a child of the plane
        GameObject wallAnchorObject = Instantiate(_wallAnchorPrefab, plane.transform);
        wallAnchorObject.name = $"Wall Anchor ({plane.trackableId})";
        wallAnchorObject.SetActive(true);
        
        // Set position and rotation relative to the plane
        wallAnchorObject.transform.localPosition = plane.center;
        wallAnchorObject.transform.localRotation = Quaternion.identity;
        
        // Scale according to plane dimensions
        wallAnchorObject.transform.localScale = new Vector3(plane.size.x, _minWallHeight, plane.size.y);
        
        // Get or add required components
        ARWallAnchor wallAnchor = wallAnchorObject.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
            wallAnchor = wallAnchorObject.AddComponent<ARWallAnchor>();
        
        // Set wall anchor properties
        wallAnchor.ARPlane = plane;
        wallAnchor.SetWallDimensions(plane.size.x, _minWallHeight);
        
        ARAnchor anchor = wallAnchorObject.GetComponent<ARAnchor>();
        if (anchor == null)
            anchor = wallAnchorObject.AddComponent<ARAnchor>();
        
        // Add the wall anchor to the list
        _wallAnchors.Add(wallAnchor);
        
        if (_debugMode)
            Debug.Log($"RemaluxARWallSetup: Created wall anchor for plane {plane.trackableId} with size {plane.size}");
    }
    
    /// <summary>
    /// Проверяет, является ли плоскость вертикальной
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
    /// Обновляет обнаружение стен в Update
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
    /// Очищает все обнаруженные стены
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
        
        // IMPORTANT: Do not destroy the prefab template!
        // This ensures _wallAnchorPrefab always remains valid for Instantiate
        
        if (_debugMode)
            Debug.Log("RemaluxARWallSetup: Cleared all wall anchors");
    }
    
    /// <summary>
    /// Возвращает список всех обнаруженных стен
    /// </summary>
    public List<ARWallAnchor> GetWallAnchors()
    {
        return _wallAnchors;
    }
} 