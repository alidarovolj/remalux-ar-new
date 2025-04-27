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
            
        // Особая обработка для ARAnchorManager - нужно найти тот, что связан с основным ARSessionOrigin
        EnsureCorrectARAnchorManager();
            
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
                Debug.Log("RemaluxARWallSetup: Using correct ARAnchorManager from the main ARSessionOrigin");
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
                Debug.Log("RemaluxARWallSetup: Successfully found ARAnchorManager on main ARSessionOrigin");
            }
            else
            {
                // Если на основном ARSessionOrigin нет ARAnchorManager, добавляем его
                _arAnchorManager = mainSessionOrigin.gameObject.AddComponent<ARAnchorManager>();
                Debug.Log("RemaluxARWallSetup: Added new ARAnchorManager to main ARSessionOrigin");
            }
        }
        else
        {
            Debug.LogWarning("RemaluxARWallSetup: No ARSessionOrigin found in the scene");
        }
    }
    
    /// <summary>
    /// Находит основной ARSessionOrigin или XROrigin в сцене
    /// </summary>
    private ARSessionOrigin FindMainARSessionOrigin()
    {
        // Поддержка как традиционного ARSessionOrigin, так и нового XROrigin
        ARSessionOrigin[] sessionOrigins = FindObjectsOfType<ARSessionOrigin>();
        
        // Сначала пытаемся найти ARSessionOrigin (для обратной совместимости)
        if (sessionOrigins.Length > 0)
        {
            if (sessionOrigins.Length == 1)
            {
                return sessionOrigins[0]; // Единственный экземпляр
            }
            
            // Если есть несколько, находим тот, у которого есть активная AR камера
            foreach (ARSessionOrigin origin in sessionOrigins)
            {
                Camera arCamera = origin.camera;
                if (arCamera != null && arCamera.gameObject.activeSelf && arCamera == Camera.main)
                {
                    Debug.Log($"RemaluxARWallSetup: Using ARSessionOrigin with the main camera: {origin.name}");
                    return origin;
                }
            }
            
            // Если не можем найти по камере, берем первый
            Debug.LogWarning("RemaluxARWallSetup: Multiple ARSessionOrigin found. Using the first one.");
            return sessionOrigins[0];
        }
        
        // Если ARSessionOrigin не найден, ищем новый XROrigin
        var xrOrigins = FindObjectsOfType<UnityEngine.XR.ARFoundation.ARSessionOrigin>();
        if (xrOrigins.Length == 0)
        {
            // Попытка найти XROrigin через полное имя типа, чтобы избежать проблем с пространством имен
            var xrOriginType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            if (xrOriginType != null)
            {
                var newXROrigins = FindObjectsOfType(xrOriginType);
                if (newXROrigins.Length > 0)
                {
                    Debug.Log("RemaluxARWallSetup: Found new XROrigin, creating compatibility wrapper");
                    
                    // Получаем объект XROrigin
                    var xrOrigin = newXROrigins[0] as Component;
                    
                    // Проверяем, есть ли у него уже компонент ARSessionOrigin
                    var existingWrapper = xrOrigin.GetComponent<ARSessionOrigin>();
                    if (existingWrapper != null)
                    {
                        return existingWrapper;
                    }
                    
                    // Создаем ARSessionOrigin на том же GameObject
                    var wrapper = xrOrigin.gameObject.AddComponent<ARSessionOrigin>();
                    
                    // Получаем камеру из XROrigin через рефлексию
                    var cameraProperty = xrOriginType.GetProperty("Camera");
                    if (cameraProperty != null)
                    {
                        wrapper.camera = cameraProperty.GetValue(xrOrigin) as Camera;
                    }
                    else
                    {
                        // Пытаемся найти камеру в дочерних объектах
                        var cameraObj = xrOrigin.transform.Find("Camera Offset/Main Camera");
                        if (cameraObj != null)
                        {
                            wrapper.camera = cameraObj.GetComponent<Camera>();
                        }
                        else
                        {
                            // Последний вариант - просто используем камеру из дочерних объектов
                            wrapper.camera = xrOrigin.GetComponentInChildren<Camera>();
                        }
                    }
                    
                    Debug.Log($"RemaluxARWallSetup: Created ARSessionOrigin wrapper for XROrigin, camera: {wrapper.camera?.name ?? "null"}");
                    return wrapper;
                }
            }
            
            Debug.LogError("RemaluxARWallSetup: No ARSessionOrigin or XROrigin found in the scene");
            return null;
        }
        
        // Если нашли ARSessionOrigin через новый тип
        if (xrOrigins.Length == 1)
        {
            return xrOrigins[0];
        }
        
        // Если нашли несколько
        foreach (var origin in xrOrigins)
        {
            Camera arCamera = origin.camera;
            if (arCamera != null && arCamera.gameObject.activeSelf && arCamera == Camera.main)
            {
                return origin;
            }
        }
        
        return xrOrigins[0];
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
        
        if (_arAnchorManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARAnchorManager component");
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
            
            // Create a basic wall anchor prefab - WITHOUT ARAnchor component
            _wallAnchorPrefab = new GameObject("Wall Anchor Template");
            _wallAnchorPrefab.AddComponent<ARWallAnchor>();
            // No longer adding ARAnchor component here - it will be created through ARAnchorManager
            
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
        
        if (_arAnchorManager == null)
        {
            Debug.LogError("RemaluxARWallSetup: Missing ARAnchorManager - cannot create proper AR anchors");
            return;
        }

        // Отладочная информация о позиции камеры до создания
        Debug.Log($"RemaluxARWallSetup: Camera BEFORE: {Camera.main.transform.position}");
        
        // Создаем якорь через ARAnchorManager, используя центр плоскости
        Pose anchorPose = new Pose(plane.center, Quaternion.LookRotation(plane.normal, Vector3.up));
        
        // Use the correct method to create an anchor based on the AR Foundation version
        ARAnchor arAnchor = null;
        
        // Try to attach anchor to the plane first (this is the preferred and most stable method)
        arAnchor = _arAnchorManager.AttachAnchor(plane, anchorPose);
        
        if (arAnchor == null)
        {
            Debug.LogWarning($"RemaluxARWallSetup: Failed to create anchor for plane {plane.trackableId} - trying alternate method");
            
            // Fallback: Try to create a free-floating anchor if available in this AR Foundation version
            try
            {
                // Some versions have this method, try using reflection to call it if available
                var methodInfo = typeof(ARAnchorManager).GetMethod("AddAnchor", new[] { typeof(Pose) });
                if (methodInfo != null)
                {
                    arAnchor = methodInfo.Invoke(_arAnchorManager, new object[] { anchorPose }) as ARAnchor;
                }
                else
                {
                    // Last resort: create a GameObject with ARAnchor manually
                    GameObject anchorGO = new GameObject($"Manual Anchor ({plane.trackableId})");
                    anchorGO.transform.position = anchorPose.position;
                    anchorGO.transform.rotation = anchorPose.rotation;
                    arAnchor = anchorGO.AddComponent<ARAnchor>();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"RemaluxARWallSetup: Error creating anchor: {ex.Message}");
            }
        }
        
        if (arAnchor == null)
        {
            Debug.LogError($"RemaluxARWallSetup: All attempts to create anchor failed for plane {plane.trackableId}");
            return;
        }
        
        // Отладочная информация о позиции якоря
        Debug.Log($"RemaluxARWallSetup: Anchor created at: {arAnchor.transform.position}");
        
        // Создаем стену из префаба и активируем ее
        GameObject wallAnchorObject = Instantiate(_wallAnchorPrefab);
        wallAnchorObject.SetActive(true);
        wallAnchorObject.name = $"Wall Anchor ({plane.trackableId})";
        
        // Устанавливаем положение и ориентацию стены НАПРЯМУЮ, без родительской связи
        wallAnchorObject.transform.position = arAnchor.transform.position;
        wallAnchorObject.transform.rotation = arAnchor.transform.rotation;
        
        // Устанавливаем родительскую связь ПОСЛЕ установки позиции и поворота
        wallAnchorObject.transform.parent = arAnchor.transform;
        
        // Устанавливаем локальное смещение в 0 для гарантии точного позиционирования
        wallAnchorObject.transform.localPosition = Vector3.zero;
        wallAnchorObject.transform.localRotation = Quaternion.identity;
        
        // Отладочный вывод после привязки
        Debug.Log($"RemaluxARWallSetup: After parenting - Wall at: {wallAnchorObject.transform.position}, anchor at: {arAnchor.transform.position}");
        
        // Масштабируем в соответствии с размерами плоскости
        float wallWidth = Mathf.Max(plane.size.x, 1.0f);
        float wallHeight = Mathf.Max(_minWallHeight, plane.size.y);
        
        // Получаем или добавляем компонент ARWallAnchor
        ARWallAnchor wallAnchor = wallAnchorObject.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
            wallAnchor = wallAnchorObject.AddComponent<ARWallAnchor>();
        
        // Устанавливаем свойства стены
        wallAnchor.ARPlane = plane;
        wallAnchor.ARAnchor = arAnchor;
        wallAnchor.SetWallDimensions(wallWidth, wallHeight);
        
        // Устанавливаем материал и цвет стены если они заданы
        if (_wallMaterial != null)
        {
            wallAnchor.SetWallMaterial(_wallMaterial);
            wallAnchor.SetWallColor(_wallColor);
        }
        
        // Добавляем стену в список
        _wallAnchors.Add(wallAnchor);
        
        // Отладочная проверка родительской связи
        if (wallAnchorObject.transform.parent != arAnchor.transform)
        {
            Debug.LogWarning("RemaluxARWallSetup: Wall parenting failed! Fixing...");
            wallAnchorObject.transform.parent = arAnchor.transform;
        }
        
        if (_debugMode)
        {
            Debug.Log($"RemaluxARWallSetup: Created wall anchor for plane {plane.trackableId} with size {wallWidth}x{wallHeight}");
            PrintObjectHierarchy(arAnchor.transform, 0);
        }
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