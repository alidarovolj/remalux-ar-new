using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using ML.DeepLab; // Add DeepLab namespace

/// <summary>
/// Attaches AR anchors to detected walls to keep them fixed in the environment
/// rather than moving with the camera
/// </summary>
[RequireComponent(typeof(ARAnchorManager))]
public class ARWallAnchor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARPlaneManager _planeManager;
    [SerializeField] private ARWallPainter _wallPainter;
    [SerializeField] private ARCameraManager _cameraManager;
    [SerializeField] private EnhancedDeepLabPredictor _predictor; // Add reference to predictor
    
    // The ARAnchorManager component responsible for creating and managing anchors
    private ARAnchorManager _anchorManager;
    
    // Dictionary to track anchors we've created for walls
    private Dictionary<TrackableId, ARAnchor> _wallAnchors = new Dictionary<TrackableId, ARAnchor>();
    // Список якорей, не привязанных к конкретным плоскостям
    private List<ARAnchor> _standaloneAnchors = new List<ARAnchor>();
    
    // Flag to control behavior - whether to create anchors automatically
    [SerializeField] private bool _autoAnchorWalls = true;
    
    // Дополнительные настройки
    [Header("Advanced Settings")]
    [SerializeField] private bool _findExistingWalls = true; // Искать существующие стены
    [SerializeField] private bool _debugMode = true;
    [SerializeField] private string[] _wallMeshNames = new string[] { "WallMesh", "Wall", "ARPlane" }; // Шаблоны имен для стен
    [SerializeField] private float _anchorUpdateInterval = 0.5f; // Interval for anchor updates
    [SerializeField] private bool _waitForARPlanes = true; // Wait for AR planes before anchoring
    [SerializeField] private float _minPlaneDetectionTime = 2.0f; // Minimum time to wait for plane detection
    
    // Компоненты Wall System в вашей сцене
    private Transform _wallDetectionSystem;
    private Transform _wallSystem;

    // Track model initialization and AR system state
    private bool _modelInitialized = false;
    private bool _arPlanesDetected = false;
    private float _startTime;
    private float _lastAnchorUpdateTime;
    
    private void Awake()
    {
        // Get required components
        _anchorManager = GetComponent<ARAnchorManager>();
        
        // Try to find components if not assigned
        if (_planeManager == null)
            _planeManager = FindObjectOfType<ARPlaneManager>();
            
        if (_wallPainter == null)
            _wallPainter = FindObjectOfType<ARWallPainter>();
            
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
        if (_cameraManager == null)
            _cameraManager = FindObjectOfType<ARCameraManager>();
            
        // Найдем компоненты Wall System
        _wallDetectionSystem = GameObject.Find("Wall Detection System")?.transform;
        _wallSystem = GameObject.Find("Wall System")?.transform;
        
        if (_debugMode)
        {
            if (_wallDetectionSystem != null)
                Debug.Log($"ARWallAnchor: Found Wall Detection System with {_wallDetectionSystem.childCount} children");
            
            if (_wallSystem != null)
                Debug.Log($"ARWallAnchor: Found Wall System with {_wallSystem.childCount} children");
        }

        _startTime = Time.time;
        _lastAnchorUpdateTime = 0f;
    }
    
    private void OnEnable()
    {
        if (_planeManager != null)
        {
            // Set plane detection mode to include vertical planes (walls)
            _planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical | PlaneDetectionMode.Horizontal;
            _planeManager.planesChanged += OnPlanesChanged;
            Debug.Log("ARWallAnchor: Subscribed to planesChanged event and enabled vertical plane detection");
        }

        // Subscribe to model initialization events
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted += OnSegmentationCompleted;
            Debug.Log("ARWallAnchor: Subscribed to segmentation events");
        }
    }
    
    private void OnDisable()
    {
        if (_planeManager != null)
        {
            _planeManager.planesChanged -= OnPlanesChanged;
        }
        
        if (_predictor != null)
        {
            _predictor.OnSegmentationCompleted -= OnSegmentationCompleted;
        }
        
        // При отключении компонента удаляем все созданные якори
        foreach (var anchor in _wallAnchors.Values)
        {
            if (anchor != null)
                Destroy(anchor.gameObject);
        }
        _wallAnchors.Clear();
        
        // Удаляем отдельные якори
        foreach (var anchor in _standaloneAnchors)
        {
            if (anchor != null)
                Destroy(anchor.gameObject);
        }
        _standaloneAnchors.Clear();
    }
    
    private void Start()
    {
        // Если активирован поиск существующих стен, запустим его через небольшую задержку
        if (_findExistingWalls)
        {
            Invoke("FindAndAnchorExistingWalls", 1.0f);
        }
    }

    private void Update()
    {
        // Check if enough time has passed for plane detection
        if (!_arPlanesDetected && Time.time - _startTime > _minPlaneDetectionTime)
        {
            int verticalPlaneCount = 0;
            
            // Count vertical planes
            if (_planeManager != null)
            {
                foreach (ARPlane plane in _planeManager.trackables)
                {
                    if (IsVerticalPlane(plane))
                    {
                        verticalPlaneCount++;
                    }
                }
            }
            
            // If we have vertical planes, set the flag
            if (verticalPlaneCount > 0 || !_waitForARPlanes)
            {
                _arPlanesDetected = true;
                Debug.Log($"ARWallAnchor: AR planes detected - found {verticalPlaneCount} vertical planes");
            }
        }

        // Update anchors periodically
        if (_arPlanesDetected && _modelInitialized && 
            Time.time - _lastAnchorUpdateTime > _anchorUpdateInterval)
        {
            UpdateWallAnchors();
            _lastAnchorUpdateTime = Time.time;
        }
    }

    // Called when the segmentation model completes processing
    private void OnSegmentationCompleted(Texture2D segmentationTexture)
    {
        if (!_modelInitialized)
        {
            _modelInitialized = true;
            Debug.Log("ARWallAnchor: Segmentation model has been initialized");
        }
    }
    
    // Update wall anchors based on the latest segmentation and AR planes
    private void UpdateWallAnchors()
    {
        if (_wallSystem == null) return;

        // Process all wall objects from the Wall System
        for (int i = 0; i < _wallSystem.childCount; i++)
        {
            Transform wallTransform = _wallSystem.GetChild(i);
            if (IsWallObject(wallTransform.gameObject))
            {
                // Find closest vertical plane for this wall
                ARPlane closestPlane = FindClosestVerticalPlane(wallTransform.position);
                
                if (closestPlane != null)
                {
                    // Create anchor for this plane if doesn't exist
                    if (!_wallAnchors.ContainsKey(closestPlane.trackableId))
                    {
                        CreateWallAnchor(closestPlane);
                    }
                    
                    // Attach wall to anchor
                    if (_wallAnchors.TryGetValue(closestPlane.trackableId, out ARAnchor anchor))
                    {
                        AttachToAnchor(wallTransform, anchor.transform);
                        if (_debugMode)
                            Debug.Log($"ARWallAnchor: Attached wall {wallTransform.name} to anchor for plane {closestPlane.trackableId}");
                    }
                }
                else
                {
                    // Create standalone anchor if needed
                    bool hasAnchor = false;
                    foreach (var anchor in _standaloneAnchors)
                    {
                        if (anchor.transform.childCount > 0 && 
                            anchor.transform.GetChild(0) == wallTransform)
                        {
                            hasAnchor = true;
                            break;
                        }
                    }
                    
                    if (!hasAnchor)
                    {
                        GameObject anchorObj = new GameObject($"Wall Anchor for {wallTransform.name}");
                        anchorObj.transform.position = wallTransform.position;
                        anchorObj.transform.rotation = wallTransform.rotation;
                        ARAnchor anchor = anchorObj.AddComponent<ARAnchor>();
                        
                        if (anchor != null)
                        {
                            AttachToAnchor(wallTransform, anchor.transform);
                            _standaloneAnchors.Add(anchor);
                            if (_debugMode)
                                Debug.Log($"ARWallAnchor: Created standalone anchor for wall {wallTransform.name}");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Поиск и закрепление уже существующих стен в сцене
    /// </summary>
    private void FindAndAnchorExistingWalls()
    {
        if (_debugMode)
            Debug.Log("ARWallAnchor: Searching for existing walls in scene");
        
        // Проверим, есть ли стены в Wall System
        List<GameObject> existingWalls = new List<GameObject>();
        
        // Проверим Wall System если он существует
        if (_wallSystem != null)
        {
            for (int i = 0; i < _wallSystem.childCount; i++)
            {
                Transform child = _wallSystem.GetChild(i);
                if (IsWallObject(child.gameObject))
                {
                    existingWalls.Add(child.gameObject);
                    if (_debugMode)
                        Debug.Log($"ARWallAnchor: Found wall in Wall System: {child.name}");
                }
            }
        }
        
        // Проверим Wall Detection System если он существует
        if (_wallDetectionSystem != null)
        {
            // Ищем стены в Wall Detection System
            for (int i = 0; i < _wallDetectionSystem.childCount; i++)
            {
                Transform child = _wallDetectionSystem.GetChild(i);
                
                // Если это стена - добавляем в список
                if (IsWallObject(child.gameObject))
                {
                    existingWalls.Add(child.gameObject);
                    if (_debugMode)
                        Debug.Log($"ARWallAnchor: Found wall in Wall Detection System: {child.name}");
                }
                
                // Проверяем и дочерние объекты
                for (int j = 0; j < child.childCount; j++)
                {
                    Transform grandchild = child.GetChild(j);
                    if (IsWallObject(grandchild.gameObject))
                    {
                        existingWalls.Add(grandchild.gameObject);
                        if (_debugMode)
                            Debug.Log($"ARWallAnchor: Found wall in {child.name}: {grandchild.name}");
                    }
                }
            }
        }
        
        // Найдем все существующие AR плоскости
        if (_planeManager != null)
        {
            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (IsVerticalPlane(plane))
                {
                    // Создадим якорь для этой плоскости
                    CreateWallAnchor(plane);
                    
                    // Проверим, есть ли у этой плоскости ребенок - стена
                    for (int i = 0; i < plane.transform.childCount; i++)
                    {
                        Transform child = plane.transform.GetChild(i);
                        if (IsWallObject(child.gameObject))
                        {
                            // Эта стена уже является ребенком плоскости, переместим к якорю
                            if (_wallAnchors.TryGetValue(plane.trackableId, out ARAnchor anchor))
                            {
                                AttachToAnchor(child, anchor.transform);
                                if (_debugMode)
                                    Debug.Log($"ARWallAnchor: Attached existing wall {child.name} to anchor");
                            }
                        }
                    }
                }
            }
        }
        
        // Для всех найденных отдельно стоящих стен
        foreach (GameObject wallObj in existingWalls)
        {
            // Найдем ближайшую вертикальную AR плоскость
            ARPlane closestPlane = FindClosestVerticalPlane(wallObj.transform.position);
            if (closestPlane != null)
            {
                // Создадим якорь для этой плоскости, если его еще нет
                if (!_wallAnchors.ContainsKey(closestPlane.trackableId))
                {
                    CreateWallAnchor(closestPlane);
                }
                
                // Привяжем стену к якорю
                if (_wallAnchors.TryGetValue(closestPlane.trackableId, out ARAnchor anchor))
                {
                    AttachToAnchor(wallObj.transform, anchor.transform);
                    if (_debugMode)
                        Debug.Log($"ARWallAnchor: Attached wall {wallObj.name} to nearest plane anchor");
                }
            }
            else
            {
                // Если нет подходящей плоскости, создадим отдельный якорь
                GameObject anchorObj = new GameObject($"Wall Anchor for {wallObj.name}");
                anchorObj.transform.position = wallObj.transform.position;
                anchorObj.transform.rotation = wallObj.transform.rotation;
                ARAnchor anchor = anchorObj.AddComponent<ARAnchor>();
                if (anchor != null)
                {
                    AttachToAnchor(wallObj.transform, anchor.transform);
                    // Добавляем в список отдельных якорей
                    _standaloneAnchors.Add(anchor);
                    if (_debugMode)
                        Debug.Log($"ARWallAnchor: Created standalone anchor for wall {wallObj.name}");
                }
            }
        }
    }
    
    /// <summary>
    /// Проверяет, является ли данный объект стеной
    /// </summary>
    private bool IsWallObject(GameObject obj)
    {
        // Проверка по наличию меша
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        
        if (meshFilter != null && renderer != null && meshFilter.sharedMesh != null)
        {
            // Проверяем имя объекта на соответствие шаблонам для стен
            foreach (string wallName in _wallMeshNames)
            {
                if (obj.name.Contains(wallName))
                    return true;
            }
            
            // Проверим имя материала - часто для стен используют специальные материалы
            if (renderer.sharedMaterial != null)
            {
                string matName = renderer.sharedMaterial.name.ToLower();
                if (matName.Contains("wall") || matName.Contains("plane") || matName.Contains("surface"))
                    return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Находит ближайшую вертикальную AR плоскость к указанной позиции
    /// </summary>
    private ARPlane FindClosestVerticalPlane(Vector3 position)
    {
        if (_planeManager == null)
            return null;
            
        ARPlane closestPlane = null;
        float closestDistance = float.MaxValue;
        
        foreach (ARPlane plane in _planeManager.trackables)
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
        
        // Если ближайшая плоскость слишком далеко, не используем её
        if (closestDistance > 1.0f) // Максимальное расстояние 1 метр
        {
            return null;
        }
        
        return closestPlane;
    }
    
    /// <summary>
    /// Прикрепляет объект к якорю, сохраняя его мировую позицию и поворот
    /// </summary>
    private void AttachToAnchor(Transform objTransform, Transform anchorTransform)
    {
        // Запомним мировые координаты
        Vector3 worldPos = objTransform.position;
        Quaternion worldRot = objTransform.rotation;
        Vector3 worldScale = objTransform.lossyScale;
        
        // Сделаем объект ребенком якоря
        objTransform.SetParent(anchorTransform, false);
        
        // Восстановим мировые координаты
        objTransform.position = worldPos;
        objTransform.rotation = worldRot;
        
        // Пытаемся сохранить масштаб (может потребоваться корректировка)
        Vector3 newLocalScale = objTransform.localScale;
        if (anchorTransform.lossyScale.x != 0 && anchorTransform.lossyScale.y != 0 && anchorTransform.lossyScale.z != 0)
        {
            newLocalScale.x = worldScale.x / anchorTransform.lossyScale.x;
            newLocalScale.y = worldScale.y / anchorTransform.lossyScale.y;
            newLocalScale.z = worldScale.z / anchorTransform.lossyScale.z;
            objTransform.localScale = newLocalScale;
        }
    }
    
    /// <summary>
    /// Handle plane detection changes
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Process added and updated planes
        ProcessNewPlanes(args.added);
        ProcessUpdatedPlanes(args.updated);
        
        // Clean up removed planes
        foreach (ARPlane plane in args.removed)
        {
            RemoveWallAnchor(plane.trackableId);
        }

        // Count vertical planes to check if we have detected walls
        int verticalPlaneCount = 0;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (IsVerticalPlane(plane))
            {
                verticalPlaneCount++;
            }
        }

        if (verticalPlaneCount > 0 && !_arPlanesDetected)
        {
            _arPlanesDetected = true;
            if (_debugMode)
                Debug.Log($"ARWallAnchor: AR vertical planes detected - found {verticalPlaneCount} vertical planes");
        }
    }
    
    /// <summary>
    /// Process newly detected planes
    /// </summary>
    private void ProcessNewPlanes(List<ARPlane> planes)
    {
        if (!_autoAnchorWalls) return;
        
        foreach (ARPlane plane in planes)
        {
            // Only create anchors for vertical planes (potential walls)
            if (IsVerticalPlane(plane))
            {
                // If wall detection is active, check if this plane is identified as a wall
                if (_wallPainter != null && !IsWallPlane(plane))
                {
                    continue; // Skip non-wall planes
                }
                
                CreateWallAnchor(plane);
            }
        }
    }
    
    /// <summary>
    /// Process updated planes
    /// </summary>
    private void ProcessUpdatedPlanes(List<ARPlane> planes)
    {
        if (!_autoAnchorWalls) return;
        
        foreach (ARPlane plane in planes)
        {
            // If the plane is now vertical but wasn't before, add an anchor
            if (IsVerticalPlane(plane) && !_wallAnchors.ContainsKey(plane.trackableId))
            {
                // Check if it's a wall (if wall painter exists)
                if (_wallPainter != null && !IsWallPlane(plane))
                {
                    continue; // Skip non-wall planes
                }
                
                CreateWallAnchor(plane);
            }
            // If the plane was vertical but is no longer, remove the anchor
            else if (!IsVerticalPlane(plane) && _wallAnchors.ContainsKey(plane.trackableId))
            {
                RemoveWallAnchor(plane.trackableId);
            }
            // If we have an anchor for this plane, update it
            else if (_wallAnchors.ContainsKey(plane.trackableId))
            {
                UpdateWallAnchor(plane);
            }
        }
    }
    
    /// <summary>
    /// Create an AR anchor for a wall plane
    /// </summary>
    private void CreateWallAnchor(ARPlane plane)
    {
        if (_anchorManager == null || plane == null) return;
        
        // Skip if we already have an anchor for this plane
        if (_wallAnchors.ContainsKey(plane.trackableId)) return;
        
        // Create an anchor at the plane's center
        ARAnchor anchor = _anchorManager.AttachAnchor(plane, new Pose(plane.center, plane.transform.rotation));
        
        if (anchor != null)
        {
            // Store the anchor reference
            _wallAnchors[plane.trackableId] = anchor;
            
            // If the wall visualization is a child of the plane, move it to be a child of the anchor
            Transform wallVisualization = plane.transform.Find("WallVisualization");
            if (wallVisualization != null)
            {
                AttachToAnchor(wallVisualization, anchor.transform);
            }
            
            // Проверим, есть ли у плоскости дети, которые являются стенами
            for (int i = 0; i < plane.transform.childCount; i++)
            {
                Transform child = plane.transform.GetChild(i);
                if (IsWallObject(child.gameObject))
                {
                    AttachToAnchor(child, anchor.transform);
                    if (_debugMode)
                        Debug.Log($"ARWallAnchor: Attached child wall {child.name} to anchor");
                }
            }
            
            if (_debugMode)
                Debug.Log($"ARWallAnchor: Created anchor for wall plane {plane.trackableId}");
        }
        else
        {
            Debug.LogWarning($"ARWallAnchor: Failed to create anchor for wall plane {plane.trackableId}");
        }
    }
    
    /// <summary>
    /// Update an existing wall anchor
    /// </summary>
    private void UpdateWallAnchor(ARPlane plane)
    {
        // This is called when a plane updates but already has an anchor
        // In most cases, we don't need to do anything as the anchor system
        // will keep the anchored content in place even as the plane updates
        
        // However, if you need to make adjustments based on updated plane data,
        // you would do that here
    }
    
    /// <summary>
    /// Remove an AR anchor for a wall plane
    /// </summary>
    private void RemoveWallAnchor(TrackableId planeId)
    {
        if (_wallAnchors.TryGetValue(planeId, out ARAnchor anchor))
        {
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
                if (_debugMode)
                    Debug.Log($"ARWallAnchor: Removed anchor for wall plane {planeId}");
            }
            
            _wallAnchors.Remove(planeId);
        }
    }
    
    /// <summary>
    /// Check if a plane is vertical (potential wall)
    /// </summary>
    private bool IsVerticalPlane(ARPlane plane)
    {
        if (plane == null) return false;
        
        // Check plane alignment
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            return true;
        }
        
        // Additional check using normal vector
        Vector3 normal = plane.normal;
        float dotWithUp = Vector3.Dot(normal, Vector3.up);
        
        // If the dot product with up is close to 0, the plane is vertical
        return Mathf.Abs(dotWithUp) < 0.3f;
    }
    
    /// <summary>
    /// Check if a plane is identified as a wall by the wall detection system
    /// </summary>
    private bool IsWallPlane(ARPlane plane)
    {
        // Эта функция должна определять, является ли плоскость стеной
        // В вашей системе это может быть определено другим способом
        
        // Базовая проверка - вертикальная плоскость
        if (!IsVerticalPlane(plane)) return false;
        
        // Если у нас есть WallPainter, проверим его логику
        if (_wallPainter != null)
        {
            // Здесь должна быть проверка через вашу систему
            // Поскольку _wallPainter может не иметь прямого API, используем общую проверку
            return true; // Считаем все вертикальные плоскости стенами
        }
        
        // По умолчанию считаем все вертикальные плоскости стенами
        return true;
    }
    
    /// <summary>
    /// Manually create an anchor for a specific wall plane
    /// </summary>
    public void AnchorWallPlane(TrackableId planeId)
    {
        if (_planeManager == null) return;
        
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (plane.trackableId.Equals(planeId))
            {
                CreateWallAnchor(plane);
                break;
            }
        }
    }
    
    /// <summary>
    /// Manually remove an anchor for a specific wall plane
    /// </summary>
    public void UnanchorWallPlane(TrackableId planeId)
    {
        RemoveWallAnchor(planeId);
    }
    
    /// <summary>
    /// Добавляет якори ко всем вертикальным плоскостям и перемещает существующие стены
    /// Public-метод для вызова из других скриптов или через Unity Events
    /// </summary>
    public void AnchorAllWalls()
    {
        FindAndAnchorExistingWalls();
    }
} 