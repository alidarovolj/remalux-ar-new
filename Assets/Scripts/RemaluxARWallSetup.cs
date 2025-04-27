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
    /// Реагирует на завершение сегментации изображения
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
            Debug.Log($"RemaluxARWallSetup: Created wall anchor for plane {plane.trackableId}");
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