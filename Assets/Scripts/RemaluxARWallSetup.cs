using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ML.DeepLab;
using UnityEngine.Events;
using UnityEditor;

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
            var sessionOrigin = _arAnchorManager.GetComponentInParent<ARSessionOrigin>();
            
            if (sessionOrigin != null && sessionOrigin == FindMainARSessionOrigin())
            {
                Debug.Log("[НАСТРОЙКА] ARAnchorManager уже корректно настроен");
                
                // Проверяем, что в менеджере задан префаб якоря
                if (_arAnchorManager.anchorPrefab == null && _wallAnchorPrefab != null)
                {
                    _arAnchorManager.anchorPrefab = _wallAnchorPrefab;
                    Debug.Log("[НАСТРОЙКА] Установлен префаб для ARAnchorManager из wallAnchorPrefab");
                }
                
                return; // Уже правильный ARAnchorManager
            }
        }
        
        // Ищем основной ARSessionOrigin
        var mainSessionOrigin = FindMainARSessionOrigin();
        if (mainSessionOrigin != null)
        {
            // Ищем или добавляем ARAnchorManager
            ARAnchorManager anchorManager = mainSessionOrigin.GetComponent<ARAnchorManager>();
            if (anchorManager != null)
            {
                _arAnchorManager = anchorManager;
                Debug.Log("[НАСТРОЙКА] Найден существующий ARAnchorManager на ARSessionOrigin");
            }
            else
            {
                // Добавляем ARAnchorManager на ARSessionOrigin
                _arAnchorManager = mainSessionOrigin.gameObject.AddComponent<ARAnchorManager>();
                Debug.Log("[НАСТРОЙКА] Добавлен новый ARAnchorManager на ARSessionOrigin");
            }
            
            // Устанавливаем префаб якоря для ARAnchorManager
            if (_arAnchorManager.anchorPrefab == null && _wallAnchorPrefab != null)
            {
                _arAnchorManager.anchorPrefab = _wallAnchorPrefab;
                Debug.Log("[НАСТРОЙКА] Установлен префаб якоря для ARAnchorManager");
            }
        }
        else
        {
            Debug.LogError("[НАСТРОЙКА] Не найден ARSessionOrigin в сцене");
        }
    }
    
    /// <summary>
    /// Находит основной ARSessionOrigin в сцене
    /// </summary>
    private ARSessionOrigin FindMainARSessionOrigin()
    {
        // Поддержка только ARSessionOrigin для обратной совместимости
        var sessionOrigins = FindObjectsOfType<ARSessionOrigin>();
        if (sessionOrigins.Length > 0)
        {
            if (sessionOrigins.Length == 1)
            {
                Debug.Log($"[НАСТРОЙКА] Найден ARSessionOrigin: {sessionOrigins[0].name}");
                return sessionOrigins[0];
            }
            
            // Если найдено несколько, ищем с главной камерой
            foreach (var origin in sessionOrigins)
            {
                Camera arCamera = origin.camera;
                if (arCamera != null && arCamera.gameObject.activeSelf && arCamera == Camera.main)
                {
                    Debug.Log($"[НАСТРОЙКА] Найден ARSessionOrigin с главной камерой: {origin.name}");
                    return origin;
                }
            }
            
            // Если не нашли подходящую, используем первую
            Debug.Log($"[НАСТРОЙКА] Найдено несколько ARSessionOrigin, использую первый: {sessionOrigins[0].name}");
            return sessionOrigins[0];
        }
        
        Debug.LogError("[НАСТРОЙКА] Не найден ARSessionOrigin в сцене!");
        return null;
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
        if (_debugMode)
        {
            Debug.Log($"[ЯКОРЬ] Начинаю создание якоря для плоскости {plane.trackableId}");
        }

        // Проверяем, нет ли уже существующего якоря для этой плоскости
        foreach (var existingAnchor in _wallAnchors)
        {
            if (existingAnchor != null && existingAnchor.ARPlane != null && 
                existingAnchor.ARPlane.trackableId == plane.trackableId)
            {
                Debug.Log($"[ЯКОРЬ] Якорь для плоскости {plane.trackableId} уже существует");
                return;
            }
        }

        // Проверка необходимых компонентов
        if (_wallAnchorPrefab == null)
        {
            ValidateComponents();
            if (_wallAnchorPrefab == null)
            {
                Debug.LogError("[ЯКОРЬ] Отсутствует префаб стены - не могу создать якорь");
                return;
            }
        }
        
        if (_arAnchorManager == null)
        {
            Debug.LogError("[ЯКОРЬ] Отсутствует ARAnchorManager - не могу создать AR якорь");
            
            // Пытаемся найти ARAnchorManager автоматически
            var sessionOrigin = FindMainARSessionOrigin();
            if (sessionOrigin != null)
            {
                _arAnchorManager = sessionOrigin.GetComponent<ARAnchorManager>();
                if (_arAnchorManager == null)
                {
                    Debug.LogError("[ЯКОРЬ] Не удалось найти ARAnchorManager даже на ARSessionOrigin");
                    return;
                }
                else
                {
                    Debug.Log("[ЯКОРЬ] Найден ARAnchorManager на ARSessionOrigin");
                }
            }
            else
            {
                return;
            }
        }
        
        // Создаем позу для якоря в центре плоскости с ориентацией по нормали
        Pose anchorPose = new Pose(plane.center, Quaternion.LookRotation(plane.normal, Vector3.up));
        
        // Логируем данные плоскости для отладки
        Debug.Log($"[ЯКОРЬ] Плоскость: позиция={plane.center}, нормаль={plane.normal}, размер={plane.size}");
        
        // Создаем якорь через ARAnchorManager
        ARAnchor arAnchor = null;
        
        // Метод 1: привязываем якорь к плоскости (предпочтительный способ)
        arAnchor = _arAnchorManager.AttachAnchor(plane, anchorPose);
        
        if (arAnchor == null)
        {
            Debug.LogWarning("[ЯКОРЬ] Не удалось привязать якорь к плоскости, пробую создать свободный якорь");
            
            // Метод 2: пробуем создать свободный якорь (если доступно в этой версии)
            try
            {
                // В новых версиях AR Foundation метод может называться AddAnchor
                var addAnchorMethod = typeof(ARAnchorManager).GetMethod("AddAnchor", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(Pose) }, null);
                
                if (addAnchorMethod != null)
                {
                    arAnchor = addAnchorMethod.Invoke(_arAnchorManager, new object[] { anchorPose }) as ARAnchor;
                    Debug.Log("[ЯКОРЬ] Создан свободный якорь через AddAnchor");
                }
                else
                {
                    // Метод 3: создаем якорь вручную как последний вариант
                    GameObject anchorGO = new GameObject($"Manual Anchor ({plane.trackableId})");
                    anchorGO.transform.position = anchorPose.position;
                    anchorGO.transform.rotation = anchorPose.rotation;
                    arAnchor = anchorGO.AddComponent<ARAnchor>();
                    Debug.Log("[ЯКОРЬ] Создан ручной якорь без привязки к AR системе");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ЯКОРЬ] Ошибка при создании якоря: {ex.Message}");
            }
        }
        
        // Проверяем, что якорь был успешно создан
        if (arAnchor == null)
        {
            Debug.LogError("[ЯКОРЬ] Все попытки создать якорь не удались!");
            return;
        }
        
        Debug.Log($"[ЯКОРЬ] Успешно создан якорь: {arAnchor.name} в позиции {arAnchor.transform.position}");
        
        // Создаем визуализацию стены, привязанную к якорю
        GameObject wallAnchorObject;
        
        // Если у якоря уже есть компонент ARWallAnchor, используем его
        ARWallAnchor wallAnchor = arAnchor.GetComponent<ARWallAnchor>();
        if (wallAnchor != null)
        {
            wallAnchorObject = arAnchor.gameObject;
            Debug.Log("[ЯКОРЬ] Использую существующий компонент ARWallAnchor на якоре");
        }
        else
        {
            // Создаем объект стены из префаба
            wallAnchorObject = Instantiate(_wallAnchorPrefab, arAnchor.transform);
            wallAnchorObject.name = $"Wall Anchor ({plane.trackableId})";
            wallAnchorObject.transform.localPosition = Vector3.zero;
            wallAnchorObject.transform.localRotation = Quaternion.identity;
            wallAnchorObject.SetActive(true);
            
            // Получаем компонент ARWallAnchor
            wallAnchor = wallAnchorObject.GetComponent<ARWallAnchor>();
            if (wallAnchor == null)
            {
                wallAnchor = wallAnchorObject.AddComponent<ARWallAnchor>();
            }
        }
        
        // Устанавливаем размеры стены в соответствии с размерами плоскости
        float wallWidth = Mathf.Max(plane.size.x, 1.0f);
        float wallHeight = Mathf.Max(_minWallHeight, plane.size.y * 1.5f); // Увеличиваем высоту для лучшей видимости
        
        // Включаем отладку
        wallAnchor.DebugMode = true;
        
        // Устанавливаем явные ссылки на AR компоненты
        wallAnchor.ARPlane = plane;
        wallAnchor.ARAnchor = arAnchor;
        
        // Устанавливаем размеры и материал
        wallAnchor.SetWallDimensions(wallWidth, wallHeight);
        
        if (_wallMaterial != null)
        {
            wallAnchor.SetWallMaterial(_wallMaterial);
            wallAnchor.SetWallColor(_wallColor);
        }
        
        // Добавляем созданную стену в список
        _wallAnchors.Add(wallAnchor);
        
        Debug.Log($"[ЯКОРЬ] Создана привязка стены размером {wallWidth}x{wallHeight}");
        
        // Логируем иерархию объектов для отладки
        if (_debugMode)
        {
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
        if (plane == null) return false;
        
        // Проверка по классификации
        if (plane.classification == PlaneClassification.Wall)
        {
            if (_debugMode) Debug.Log($"[СТЕНА-ПРОВЕРКА] Плоскость {plane.trackableId} классифицирована как стена");
            return true;
        }
        
        // Проверка по выравниванию
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            if (_debugMode) Debug.Log($"[СТЕНА-ПРОВЕРКА] Плоскость {plane.trackableId} имеет вертикальное выравнивание");
            return true;
        }
        
        // Проверка по вектору нормали
        Vector3 normal = plane.normal;
        float dotProduct = Vector3.Dot(normal, Vector3.up);
        float threshold = 0.3f;  // Можно регулировать порог для точности
        
        bool isVerticalByNormal = Mathf.Abs(dotProduct) < threshold;
        
        if (isVerticalByNormal)
        {
            if (_debugMode) Debug.Log($"[СТЕНА-ПРОВЕРКА] Плоскость {plane.trackableId} вертикальна по нормали: {normal}, dot: {dotProduct}");
        }
        
        return isVerticalByNormal;
    }
    
    /// <summary>
    /// Добавим метод для регулярной проверки и создания стен
    /// </summary>
    private void CheckVerticalPlanesAndCreateWalls()
    {
        if (_arPlaneManager == null)
        {
            Debug.LogError("[СТЕНА] ARPlaneManager не найден - не могу проверить плоскости");
            return;
        }
        
        // Отладочный вывод - список всех найденных плоскостей
        if (_debugMode)
        {
            Debug.Log($"[СТЕНА-ДИАГНОСТИКА] Найдено {_arPlaneManager.trackables.count} плоскостей:");
            int verticalCount = 0;
            
            foreach (var plane in _arPlaneManager.trackables)
            {
                bool isVertical = IsVerticalPlane(plane);
                Debug.Log($"[СТЕНА-ДИАГНОСТИКА] Плоскость {plane.trackableId} - размер: {plane.size}, " +
                          $"выравнивание: {plane.alignment}, вертикальная: {isVertical}, " +
                          $"нормаль: {plane.normal}, центр: {plane.center}");
                
                if (isVertical) verticalCount++;
            }
            
            Debug.Log($"[СТЕНА-ДИАГНОСТИКА] Всего найдено {verticalCount} вертикальных плоскостей");
        }
        
        // Очистим от null-ссылок
        _wallAnchors.RemoveAll(w => w == null);
        
        // Попытка создать стены для всех вертикальных плоскостей
        foreach (var plane in _arPlaneManager.trackables)
        {
            if (IsVerticalPlane(plane))
            {
                // Проверяем, нет ли уже стены для этой плоскости
                bool alreadyExists = false;
                foreach (var existingWall in _wallAnchors)
                {
                    if (existingWall != null && existingWall.ARPlane != null && 
                        existingWall.ARPlane.trackableId == plane.trackableId)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                
                if (!alreadyExists)
                {
                    Debug.Log($"[СТЕНА-ДИАГНОСТИКА] Создаю стену для новой вертикальной плоскости {plane.trackableId}");
                    CreateWallAnchorForPlane(plane);
                }
            }
        }
        
        // Выводим текущий статус стен
        LogCurrentWalls();
    }
    
    /// <summary>
    /// Обновляет обнаружение стен в Update
    /// </summary>
    private void Update()
    {
        if (!_isTracking) return;
        
        // Периодически проверяем плоскости и создаем стены
        if (Time.time - _lastDetectionTime > _wallDetectionInterval)
        {
            _lastDetectionTime = Time.time;
            
            // Запускаем нашу новую диагностику
            CheckVerticalPlanesAndCreateWalls();
            
            // Продолжаем использовать существующую логику
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
    
    private void LogCurrentWalls()
    {
        Debug.Log($"[СТЕНА-СТАТУС] ======= ТЕКУЩИЕ СТЕНЫ ({_wallAnchors.Count}) =======");
        
        int validCount = 0;
        int index = 0;
        
        foreach (var wall in _wallAnchors)
        {
            if (wall == null)
            {
                Debug.Log($"[СТЕНА-СТАТУС] #{index}: NULL");
                continue;
            }
            
            string planeInfo = wall.ARPlane != null ? 
                $"ID:{wall.ARPlane.trackableId}, размер:{wall.ARPlane.size}" : "NULL";
            
            string anchorInfo = wall.ARAnchor != null ? 
                $"позиция:{wall.ARAnchor.transform.position}" : "NULL";
            
            bool isActive = wall.gameObject.activeInHierarchy;
            bool hasWallObject = wall.Wall != null;
            
            Debug.Log($"[СТЕНА-СТАТУС] #{index}: {wall.name} - Active:{isActive}, Valid:{wall.IsValid}, " +
                     $"Wall:{hasWallObject}, Plane:{planeInfo}, Anchor:{anchorInfo}");
            
            if (wall.IsValid) validCount++;
            index++;
        }
        
        Debug.Log($"[СТЕНА-СТАТУС] Всего активных стен: {validCount} из {_wallAnchors.Count}");
        Debug.Log($"[СТЕНА-СТАТУС] =================================");
    }

    /// <summary>
    /// Создает тестовые стены с фиксированными позициями для отладки
    /// </summary>
    public void CreateDebugWalls()
    {
        Debug.Log("[ТЕСТ] Создаю тестовые стены для отладки");
        
        // Получаем позицию камеры
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[ТЕСТ] Камера не найдена!");
            return;
        }
        
        Vector3 cameraPos = mainCamera.transform.position;
        Vector3 cameraForward = mainCamera.transform.forward;
        
        Debug.Log($"[ТЕСТ] Позиция камеры: {cameraPos}, направление: {cameraForward}");
        
        // Очищаем все существующие стены для чистоты эксперимента
        ClearAllWalls();
        
        // Создаем 4 тестовые стены вокруг камеры
        float distance = 2.0f; // Расстояние от камеры
        float wallWidth = 2.0f;
        float wallHeight = 2.5f;
        
        // Создаем стену спереди
        CreateTestWall(
            cameraPos + cameraForward * distance,
            Quaternion.LookRotation(-cameraForward), // Развернута к камере
            wallWidth,
            wallHeight,
            Color.red,
            "Front Wall"
        );
        
        // Создаем стену справа
        Vector3 rightDir = Vector3.Cross(Vector3.up, cameraForward).normalized;
        CreateTestWall(
            cameraPos + rightDir * distance,
            Quaternion.LookRotation(-rightDir), // Развернута к камере
            wallWidth,
            wallHeight,
            Color.green,
            "Right Wall"
        );
        
        // Создаем стену слева
        CreateTestWall(
            cameraPos - rightDir * distance,
            Quaternion.LookRotation(rightDir), // Развернута к камере
            wallWidth,
            wallHeight,
            Color.blue,
            "Left Wall"
        );
        
        // Создаем стену сзади
        CreateTestWall(
            cameraPos - cameraForward * distance,
            Quaternion.LookRotation(cameraForward), // Развернута к камере
            wallWidth,
            wallHeight,
            Color.yellow,
            "Back Wall"
        );
        
        Debug.Log("[ТЕСТ] Создано 4 тестовые стены");
    }

    /// <summary>
    /// Создает тестовую стену в указанной позиции
    /// </summary>
    private void CreateTestWall(Vector3 position, Quaternion rotation, float width, float height, Color color, string name)
    {
        if (_wallAnchorPrefab == null)
            ValidateComponents();
        
        if (_wallAnchorPrefab == null)
        {
            Debug.LogError("[ТЕСТ] Не задан префаб для стены!");
            return;
        }
        
        // Создаем объект стены
        GameObject wallObject = Instantiate(_wallAnchorPrefab);
        wallObject.SetActive(true);
        wallObject.name = name;
        
        // Устанавливаем позицию и поворот
        wallObject.transform.position = position;
        wallObject.transform.rotation = rotation;
        
        // Получаем компонент ARWallAnchor
        ARWallAnchor wallAnchor = wallObject.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
            wallAnchor = wallObject.AddComponent<ARWallAnchor>();
        
        // Включаем отладку
        wallAnchor.DebugMode = true;
        
        // Устанавливаем размеры стены
        wallAnchor.SetWallDimensions(width, height);
        
        // Создаем материал с указанным цветом
        Material wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMaterial.color = new Color(color.r, color.g, color.b, 0.7f);
        
        // Настраиваем материал для прозрачности
        wallMaterial.SetFloat("_Surface", 1);  // 1 = прозрачный
        wallMaterial.SetFloat("_Blend", 0);    // 0 = альфа-смешивание
        wallMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        wallMaterial.renderQueue = 3000;       // Прозрачная очередь рендеринга
        
        // Устанавливаем материал
        wallAnchor.SetWallMaterial(wallMaterial);
        
        // Добавляем стену в список
        _wallAnchors.Add(wallAnchor);
        
        Debug.Log($"[ТЕСТ] Создана тестовая стена {name} в позиции {position}");
    }

    // Добавим публичный метод для вызова из инспектора
    public void DebugCreateTestWalls()
    {
        CreateDebugWalls();
    }
} 