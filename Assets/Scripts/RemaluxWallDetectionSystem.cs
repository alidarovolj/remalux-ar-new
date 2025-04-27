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
    [SerializeField] private ARAnchorManager _arAnchorManager;
    
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
        // Найти обязательные компоненты, если не заданы
        if (_arSession == null)
            _arSession = FindObjectOfType<ARSession>();
            
        if (_arPlaneManager == null)
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            
        // Особая обработка для ARAnchorManager - нужно найти тот, что связан с основным ARSessionOrigin
        EnsureCorrectARAnchorManager();
            
        if (_deepLabPredictor == null)
            _deepLabPredictor = FindObjectOfType<DeepLabPredictor>();
            
        if (_arCameraManager == null)
            _arCameraManager = FindObjectOfType<ARCameraManager>();
            
        if (_arRaycastManager == null)
            _arRaycastManager = FindObjectOfType<ARRaycastManager>();
            
        if (_wallAnchorConnector == null)
            _wallAnchorConnector = FindObjectOfType<WallAnchorConnector>();
            
        // Валидировать компоненты в Awake вместо Start
        ValidateComponents();
        
        // Создать список для хранения стен
        _wallAnchors = new List<ARWallAnchor>();
    }
    
    private void Start()
    {
        // If we have a legacy setup, disable it to avoid conflicts
        if (_legacySetup != null && _legacySetup != this)
        {
            Debug.Log("RemaluxWallDetectionSystem: Found legacy setup. New system will take over.");
            _legacySetup.enabled = false;
        }
        
        // Components are already validated in Awake
        
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
    /// Убеждается, что мы используем правильный экземпляр ARAnchorManager, связанный с основным ARSessionOrigin
    /// </summary>
    private void EnsureCorrectARAnchorManager()
    {
        if (_arAnchorManager != null)
        {
            // Проверяем, является ли текущий ARAnchorManager тем, что на основном ARSessionOrigin
            ARSessionOrigin sessionOrigin = _arAnchorManager.GetComponentInParent<ARSessionOrigin>();
            if (sessionOrigin != null && sessionOrigin == FindMainARSessionOrigin())
            {
                Debug.Log("RemaluxWallDetectionSystem: Using correct ARAnchorManager from the main ARSessionOrigin");
                return; // Уже правильный ARAnchorManager
            }
        }
        
        // Ищем основной ARSessionOrigin и его ARAnchorManager
        ARSessionOrigin mainSessionOrigin = FindMainARSessionOrigin();
        if (mainSessionOrigin != null)
        {
            ARAnchorManager anchorManager = mainSessionOrigin.GetComponent<ARAnchorManager>();
            if (anchorManager != null)
            {
                _arAnchorManager = anchorManager;
                Debug.Log("RemaluxWallDetectionSystem: Successfully found ARAnchorManager on main ARSessionOrigin");
            }
            else
            {
                // Если на основном ARSessionOrigin нет ARAnchorManager, добавляем его
                _arAnchorManager = mainSessionOrigin.gameObject.AddComponent<ARAnchorManager>();
                Debug.Log("RemaluxWallDetectionSystem: Added new ARAnchorManager to main ARSessionOrigin");
            }
        }
        else
        {
            Debug.LogWarning("RemaluxWallDetectionSystem: No ARSessionOrigin found in the scene");
        }
    }
    
    /// <summary>
    /// Находит основной ARSessionOrigin в сцене
    /// </summary>
    private ARSessionOrigin FindMainARSessionOrigin()
    {
        // Ищем ARSessionOrigin, у которого есть компоненты AR
        ARSessionOrigin[] sessionOrigins = FindObjectsOfType<ARSessionOrigin>();
        
        if (sessionOrigins.Length == 0)
        {
            Debug.LogError("RemaluxWallDetectionSystem: No ARSessionOrigin found in the scene");
            return null;
        }
        
        if (sessionOrigins.Length == 1)
        {
            return sessionOrigins[0]; // Единственный экземпляр
        }
        
        // Если есть несколько, находим тот, у которого есть активная AR камера
        foreach (ARSessionOrigin origin in sessionOrigins)
        {
            // Проверяем, есть ли у этого ARSessionOrigin связанная AR камера
            Camera arCamera = origin.camera;
            if (arCamera != null && arCamera.gameObject.activeSelf && arCamera == Camera.main)
            {
                Debug.Log($"RemaluxWallDetectionSystem: Using ARSessionOrigin with the main camera: {origin.name}");
                return origin;
            }
        }
        
        // Если не можем найти по камере, берем первый
        Debug.LogWarning("RemaluxWallDetectionSystem: Multiple ARSessionOrigin found. Using the first one.");
        return sessionOrigins[0];
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
        
        if (_arAnchorManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARAnchorManager component");
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
            
            // Create a basic wall anchor prefab - WITHOUT ARAnchor component
            _wallAnchorPrefab = new GameObject("Wall Anchor Template");
            _wallAnchorPrefab.AddComponent<ARWallAnchor>();
            // No longer adding ARAnchor component here
            
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
            
            // Process wall segmentation after it's complete
            CreateWallAnchorsFromSegmentation();
        }
    }
    
    /// <summary>
    /// Create wall anchors based on segmentation results
    /// </summary>
    private void CreateWallAnchorsFromSegmentation()
    {
        // Check required components
        if (_arRaycastManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: ARRaycastManager is required for wall anchoring");
            return;
        }
        
        // Get segmentation data
        Texture2D segmentationTexture = null;
        
        // Try to get segmentation texture from different sources
        if (_predictor != null)
        {
            // Try to get the texture from EnhancedDeepLabPredictor
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
                Debug.LogWarning($"RemaluxWallDetectionSystem: Error getting segmentation texture: {e.Message}");
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
                Debug.Log("RemaluxWallDetectionSystem: No segmentation texture available");
            return;
        }
        
        // Add debug log to help identify what class IDs are present in the segmentation
        if (_debugMode)
        {
            LogSegmentationHistogram(segmentationTexture);
        }
        
        // Sample points from the segmentation to locate walls
        int sampleCount = 5; // Number of samples across and down the screen
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
                            // Create wall anchor for this plane
                            CreateWallAnchorForPlane(hitPlane);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Log histogram of segmentation classes to help identify correct wall class ID
    /// </summary>
    private void LogSegmentationHistogram(Texture2D segmentationTexture)
    {
        try
        {
            // Sample the texture to create a histogram of class IDs
            Dictionary<int, int> counts = new Dictionary<int, int>();
            
            // Sample every 10th pixel to avoid too much processing
            for (int x = 0; x < segmentationTexture.width; x += 10)
            {
                for (int y = 0; y < segmentationTexture.height; y += 10)
                {
                    Color pixel = segmentationTexture.GetPixel(x, y);
                    int classId = Mathf.RoundToInt(pixel.r * 255);
                    
                    if (!counts.ContainsKey(classId))
                        counts[classId] = 0;
                        
                    counts[classId]++;
                }
            }
            
            // Create output string
            string histogram = "Segmentation histogram: ";
            foreach (var pair in counts)
            {
                histogram += $"{pair.Key}:{pair.Value}, ";
            }
            
            Debug.Log(histogram);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"RemaluxWallDetectionSystem: Cannot read segmentation texture: {e.Message}");
        }
    }
    
    /// <summary>
    /// Check if the screen position contains wall segmentation
    /// </summary>
    private bool IsWallSegmentAtScreenPos(Vector2 screenPos, Texture2D segmentationTexture)
    {
        if (segmentationTexture == null)
            return false;
        
        // Make sure to use the correct wall class ID for your model
        // For ADE20K models, wall class ID might be different than 9
        int wallClassId = 9; // Default - adjust for your specific model
        
        // Get wall class ID from predictor if available
        if (_predictor != null)
        {
            wallClassId = _predictor.WallClassId;
        }
        else if (_deepLabPredictor != null)
        {
            wallClassId = _deepLabPredictor.WallClassId;
        }
        
        try
        {
            // Convert screen position to texture coordinates
            int textureX = Mathf.FloorToInt((screenPos.x / Screen.width) * segmentationTexture.width);
            int textureY = Mathf.FloorToInt((screenPos.y / Screen.height) * segmentationTexture.height);
            
            // Ensure coordinates are within bounds
            textureX = Mathf.Clamp(textureX, 0, segmentationTexture.width - 1);
            textureY = Mathf.Clamp(textureY, 0, segmentationTexture.height - 1);
            
            // Get pixel at this position
            Color pixel = segmentationTexture.GetPixel(textureX, textureY);
            int classId = Mathf.RoundToInt(pixel.r * 255);
            
            return classId == wallClassId;
        }
        catch (System.Exception e)
        {
            // Texture might not be readable
            Debug.LogWarning($"RemaluxWallDetectionSystem: Error reading texture: {e.Message}");
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
        
        // We no longer automatically create wall anchors for all vertical planes
        // Instead, we'll only create them based on segmentation results
        
        // Just handle plane removal
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
        // если вдруг шаблон не создан — создаём на лету
        if (_wallAnchorPrefab == null)
            ValidateComponents();

        if (_wallAnchorPrefab == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: _wallAnchorPrefab всё ещё не задан — пропускаем создание якоря");
            return;
        }
        
        if (_arAnchorManager == null)
        {
            Debug.LogError("RemaluxWallDetectionSystem: Missing ARAnchorManager - cannot create proper AR anchors");
            return;
        }

        // Отладочная информация о позиции камеры до создания
        Debug.Log($"RemaluxWallDetectionSystem: Camera BEFORE: {Camera.main.transform.position}");
        
        // Создаем позу в координатной системе плоскости
        Pose anchorPose = new Pose(plane.center, plane.transform.rotation);
        
        // Создаем якорь через ARAnchorManager
        ARAnchor arAnchor = _arAnchorManager.AttachAnchor(plane, anchorPose);
        
        if (arAnchor == null)
        {
            Debug.LogWarning($"RemaluxWallDetectionSystem: Failed to attach anchor to plane {plane.trackableId}");
            return;
        }
        
        // Отладочная информация о позиции якоря
        Debug.Log($"RemaluxWallDetectionSystem: Anchor at: {arAnchor.transform.position}, camera at: {Camera.main.transform.position}");
        
        // Создаем квадрик отдельно и затем привязываем его к якорю С СОХРАНЕНИЕМ мировых координат
        GameObject wallAnchorObject = Instantiate(_wallAnchorPrefab);
        // Сначала активируем, чтобы transform проинициализировался корректно
        wallAnchorObject.SetActive(true);
        wallAnchorObject.name = $"Wall Anchor ({plane.trackableId})";
        
        // Позиционируем квадрик в мировых координатах совпадающим с якорем
        wallAnchorObject.transform.position = arAnchor.transform.position;
        wallAnchorObject.transform.rotation = arAnchor.transform.rotation;
        
        // ЗАТЕМ устанавливаем родителя, СОХРАНЯЯ мировые координаты
        wallAnchorObject.transform.SetParent(arAnchor.transform, worldPositionStays: true);
        
        // Отладочный вывод после привязки
        Debug.Log($"RemaluxWallDetectionSystem: After parenting - Quad at: {wallAnchorObject.transform.position}, anchor at: {arAnchor.transform.position}");
        
        // Масштабируем в соответствии с размерами плоскости
        // Предполагаем, что квад находится в плоскости XY с нормалью Z
        wallAnchorObject.transform.localScale = new Vector3(plane.size.x, plane.size.y, 1f);
        
        // Получаем или добавляем компонент ARWallAnchor
        ARWallAnchor wallAnchor = wallAnchorObject.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
            wallAnchor = wallAnchorObject.AddComponent<ARWallAnchor>();
        
        // Устанавливаем свойства стены
        wallAnchor.ARPlane = plane;
        wallAnchor.ARAnchor = arAnchor;
        wallAnchor.SetWallDimensions(plane.size.x, _minWallHeight);
        
        // Добавляем стену в список
        _wallAnchors.Add(wallAnchor);
        
        // Отладочный вывод иерархии
        // Это поможет проверить, как выглядит дерево объектов
        PrintObjectHierarchy(arAnchor.transform, 0);
        
        if (_debugMode)
            Debug.Log($"RemaluxWallDetectionSystem: Created wall anchor for plane {plane.trackableId} with size {plane.size}");
    }
    
    /// <summary>
    /// Отладочная функция для вывода иерархии объектов
    /// </summary>
    private void PrintObjectHierarchy(Transform obj, int depth)
    {
        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent}└─ {obj.name} (position: {obj.position}, localPosition: {obj.localPosition})");
        
        foreach (Transform child in obj)
        {
            PrintObjectHierarchy(child, depth + 1);
        }
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
        
        // IMPORTANT: Do not destroy the prefab template!
        // This ensures _wallAnchorPrefab always remains valid for Instantiate
        
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