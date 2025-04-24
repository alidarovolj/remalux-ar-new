using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using UnityEditor;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem.XR;
using ML.DeepLab;
using System.Reflection;

/// <summary>
/// Компонент для инициализации и управления AR сценой
/// </summary>
public class ARSceneInitializer : MonoBehaviour
{
    [Header("AR Components")]
    public GameObject arSessionOrigin;
    public ARSessionOrigin sessionOriginComponent;
    public Camera arCamera;
    public ARSession arSession;

    [Header("Tracking Managers")]
    public ARPlaneManager planeManager;
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    public ARPointCloudManager pointCloudManager;

    [Header("Wall Detection")]
    public WallOptimizer wallOptimizer;
    public Material wallMaterial;
    
    [Header("UI References")]
    public Canvas uiCanvas;
    
    [Header("Settings")]
    public bool enablePlaneDetection = true;
    public bool showPlanes = true;
    public bool enablePointCloud = false;
    public bool showPointCloud = false;
    public bool enableWallDetection = true;
    
    /// <summary>
    /// Инициализация при старте
    /// </summary>
    private void Start()
    {
        SetupARScene();
    }
    
    /// <summary>
    /// Настройка AR сцены
    /// </summary>
    public void SetupARScene()
    {
        Debug.Log("Настройка AR сцены...");
        
        // Проверяем и находим необходимые компоненты, если они не были заданы вручную
        FillReferences();
        
        // Включаем/отключаем функциональность согласно настройкам
        ConfigureARComponents();
        
        // Настраиваем Wall Optimizer, если он доступен
        SetupWallOptimizer();
        
        Debug.Log("Настройка AR сцены завершена.");
    }
    
    /// <summary>
    /// Заполнение ссылок на компоненты
    /// </summary>
    public void FillReferences()
    {
        // Находим AR Session Origin
        if (sessionOriginComponent == null)
        {
            sessionOriginComponent = FindObjectOfType<ARSessionOrigin>();
            if (sessionOriginComponent != null)
            {
                arSessionOrigin = sessionOriginComponent.gameObject;
            }
            else
            {
                Debug.LogWarning("ARSessionOrigin не найден в сцене!");
            }
        }
        
        // Находим AR Camera
        if (arCamera == null && sessionOriginComponent != null)
        {
            arCamera = sessionOriginComponent.camera;
        }
        
        // Находим AR Session
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
        }
        
        // Находим AR компоненты
        if (planeManager == null && sessionOriginComponent != null)
        {
            planeManager = sessionOriginComponent.GetComponent<ARPlaneManager>();
        }
        
        if (raycastManager == null && sessionOriginComponent != null)
        {
            raycastManager = sessionOriginComponent.GetComponent<ARRaycastManager>();
        }
        
        if (anchorManager == null && sessionOriginComponent != null)
        {
            anchorManager = sessionOriginComponent.GetComponent<ARAnchorManager>();
        }
        
        if (pointCloudManager == null && sessionOriginComponent != null)
        {
            pointCloudManager = sessionOriginComponent.GetComponent<ARPointCloudManager>();
        }
        
        // Находим WallOptimizer
        if (wallOptimizer == null)
        {
            wallOptimizer = FindObjectOfType<WallOptimizer>();
        }
        
        // Находим UI Canvas
        if (uiCanvas == null)
        {
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            foreach (Canvas canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    uiCanvas = canvas;
                    break;
                }
            }
        }
        
        // Загружаем материал для стен, если он не задан
        if (wallMaterial == null)
        {
            wallMaterial = Resources.Load<Material>("Materials/WallMaterial");
        }
        
        // Проверяем и исправляем настройки XR Origin и камеры
        FixXROriginAndCamera();
    }
    
    /// <summary>
    /// Исправление настроек XR Origin и камеры
    /// </summary>
    private void FixXROriginAndCamera()
    {
        // Проверяем наличие XR Origin
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin == null && sessionOriginComponent != null)
        {
            // Добавляем XROrigin, если его нет
            xrOrigin = sessionOriginComponent.gameObject.AddComponent<XROrigin>();
            Debug.Log("Добавлен XROrigin к AR Session Origin");
        }
        
        if (xrOrigin != null)
        {
            // Настраиваем основную камеру
            if (xrOrigin.Camera == null && arCamera != null)
            {
                xrOrigin.Camera = arCamera;
                Debug.Log("Установлена камера для XROrigin");
            }
            
            // Проверяем наличие Camera Floor Offset
            if (xrOrigin.CameraFloorOffsetObject == null)
            {
                // Ищем Camera Offset в дочерних элементах
                Transform cameraOffset = null;
                foreach (Transform child in xrOrigin.transform)
                {
                    if (child.name.Contains("Camera Offset") || child.name.Contains("CameraOffset"))
                    {
                        cameraOffset = child;
                        break;
                    }
                }
                
                // Если не найден, ищем родителя камеры
                if (cameraOffset == null && xrOrigin.Camera != null)
                {
                    cameraOffset = xrOrigin.Camera.transform.parent;
                }
                
                // Если всё ещё не найден, создаем новый
                if (cameraOffset == null)
                {
                    GameObject newOffset = new GameObject("Camera Offset");
                    newOffset.transform.SetParent(xrOrigin.transform);
                    newOffset.transform.localPosition = Vector3.zero;
                    cameraOffset = newOffset.transform;
                    
                    // Если есть камера, перемещаем её под Camera Offset
                    if (xrOrigin.Camera != null && xrOrigin.Camera.transform.parent != cameraOffset)
                    {
                        Vector3 cameraLocalPos = xrOrigin.Camera.transform.localPosition;
                        Quaternion cameraLocalRot = xrOrigin.Camera.transform.localRotation;
                        
                        xrOrigin.Camera.transform.SetParent(cameraOffset);
                        xrOrigin.Camera.transform.localPosition = cameraLocalPos;
                        xrOrigin.Camera.transform.localRotation = cameraLocalRot;
                    }
                }
                
                xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                Debug.Log("Настроен CameraFloorOffsetObject для XROrigin: " + cameraOffset.name);
            }
        }
        
        // Проверяем наличие Tracked Pose Driver на камере
        if (arCamera != null)
        {
            var trackedPoseDriver = arCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver == null)
            {
                // Добавляем Tracked Pose Driver
                trackedPoseDriver = arCamera.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                
                // Настраиваем Tracked Pose Driver
                trackedPoseDriver.positionAction = new UnityEngine.InputSystem.InputAction(
                    name: "Position",
                    type: UnityEngine.InputSystem.InputActionType.Value,
                    binding: "<XRHMD>/centerEyePosition",
                    expectedControlType: "Vector3"
                );
                
                trackedPoseDriver.rotationAction = new UnityEngine.InputSystem.InputAction(
                    name: "Rotation",
                    type: UnityEngine.InputSystem.InputActionType.Value,
                    binding: "<XRHMD>/centerEyeRotation",
                    expectedControlType: "Quaternion"
                );
                
                // Активируем действия
                trackedPoseDriver.positionAction.Enable();
                trackedPoseDriver.rotationAction.Enable();
                
                Debug.Log("Добавлен и настроен Tracked Pose Driver для камеры");
            }
        }
    }
    
    /// <summary>
    /// Настройка компонентов AR
    /// </summary>
    private void ConfigureARComponents()
    {
        // Настройка Plane Manager
        if (planeManager != null)
        {
            planeManager.enabled = enablePlaneDetection;
            if (planeManager.planePrefab != null)
            {
                // Настраиваем видимость плоскостей
                MeshRenderer planeRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
                if (planeRenderer != null)
                {
                    planeRenderer.enabled = showPlanes;
                }
            }
        }
        
        // Настройка Point Cloud Manager
        if (pointCloudManager != null)
        {
            pointCloudManager.enabled = enablePointCloud;
            if (pointCloudManager.pointCloudPrefab != null)
            {
                // Настраиваем видимость облака точек
                MeshRenderer pointCloudRenderer = pointCloudManager.pointCloudPrefab.GetComponent<MeshRenderer>();
                if (pointCloudRenderer != null)
                {
                    pointCloudRenderer.enabled = showPointCloud;
                }
            }
        }
    }
    
    /// <summary>
    /// Настройка Wall Optimizer
    /// </summary>
    private void SetupWallOptimizer()
    {
        if (wallOptimizer != null && enableWallDetection)
        {
            wallOptimizer.enabled = true;
            
            // Находим EnhancedDeepLabPredictor если его нет
            EnhancedDeepLabPredictor enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            if (enhancedPredictor == null)
            {
                // Ищем в ML System
                GameObject mlSystemObj = GameObject.Find("ML System");
                if (mlSystemObj != null)
                {
                    enhancedPredictor = mlSystemObj.GetComponentInChildren<EnhancedDeepLabPredictor>();
                    
                    // Если не нашли, создаем новый в ML System
                    if (enhancedPredictor == null)
                    {
                        GameObject predictorObj = new GameObject("Enhanced DeepLab Predictor");
                        predictorObj.transform.SetParent(mlSystemObj.transform);
                        enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
                        Debug.Log("Создан новый EnhancedDeepLabPredictor в ML System");
                    }
                }
                else
                {
                    // Создаем ML System если нет
                    GameObject newMlSystem = new GameObject("ML System");
                    GameObject predictorObj = new GameObject("Enhanced DeepLab Predictor");
                    predictorObj.transform.SetParent(newMlSystem.transform);
                    enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
                    Debug.Log("Создан новый ML System с EnhancedDeepLabPredictor");
                }
            }
            
            // Находим WallMeshRenderer если его нет
            WallMeshRenderer meshRenderer = FindObjectOfType<WallMeshRenderer>();
            if (meshRenderer == null)
            {
                // Ищем в AR Session Origin
                GameObject arSessionOriginObj = GameObject.Find("AR Session Origin");
                if (arSessionOriginObj != null)
                {
                    // Создаем новый объект под XR Origin
                    GameObject meshRendererObj = new GameObject("Wall Mesh Renderer");
                    meshRendererObj.transform.SetParent(arSessionOriginObj.transform);
                    meshRenderer = meshRendererObj.AddComponent<WallMeshRenderer>();
                    Debug.Log("Создан новый WallMeshRenderer в AR Session Origin");
                }
                else if (arSessionOrigin != null)
                {
                    // Используем сохраненную ссылку на AR Session Origin
                    GameObject meshRendererObj = new GameObject("Wall Mesh Renderer");
                    meshRendererObj.transform.SetParent(arSessionOrigin.transform);
                    meshRenderer = meshRendererObj.AddComponent<WallMeshRenderer>();
                    Debug.Log("Создан новый WallMeshRenderer в arSessionOrigin");
                }
                else
                {
                    // Создаем отдельно
                    GameObject meshRendererObj = new GameObject("Wall Mesh Renderer");
                    meshRenderer = meshRendererObj.AddComponent<WallMeshRenderer>();
                    Debug.Log("Создан отдельный WallMeshRenderer");
                }
            }
            
            // Устанавливаем ссылки в WallOptimizer
            // EnhancedDeepLabPredictor
            var predictorField = wallOptimizer.GetType().GetField("enhancedPredictor", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (predictorField != null)
            {
                predictorField.SetValue(wallOptimizer, enhancedPredictor);
                Debug.Log("Установлена ссылка на EnhancedDeepLabPredictor в WallOptimizer");
            }
            
            // WallMeshRenderer
            var meshRendererField = wallOptimizer.GetType().GetField("meshRenderer", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (meshRendererField != null)
            {
                meshRendererField.SetValue(wallOptimizer, meshRenderer);
                Debug.Log("Установлена ссылка на WallMeshRenderer в WallOptimizer");
            }
            
            // Устанавливаем материал для стен, если он задан
            if (wallMaterial != null)
            {
                // Use reflection to set the value if the property exists
                var wallMaterialProperty = wallOptimizer.GetType().GetField("wallMaterial");
                if (wallMaterialProperty != null)
                {
                    wallMaterialProperty.SetValue(wallOptimizer, wallMaterial);
                }
            }
            
            // Настраиваем интеграцию с AR
            // Use reflection to set the values if the properties exist
            var arSessionOriginField = wallOptimizer.GetType().GetField("arSessionOrigin");
            if (arSessionOriginField != null)
            {
                arSessionOriginField.SetValue(wallOptimizer, sessionOriginComponent);
            }
            
            var planeManagerField = wallOptimizer.GetType().GetField("arPlaneManager");
            if (planeManagerField != null)
            {
                planeManagerField.SetValue(wallOptimizer, planeManager);
            }
            
            // Проверяем, нужна ли оптимизация производительности
            bool needOptimization = QualitySettings.vSyncCount == 0 && Application.targetFrameRate > 30;
            if (needOptimization)
            {
                OptimizeWallDetection();
            }
        }
        else if (wallOptimizer != null)
        {
            wallOptimizer.enabled = false;
        }
    }
    
    /// <summary>
    /// Оптимизация детекции стен
    /// </summary>
    private void OptimizeWallDetection()
    {
        if (wallOptimizer != null)
        {
            // Увеличиваем интервал обновления для экономии ресурсов
            var updateIntervalField = wallOptimizer.GetType().GetField("updateInterval");
            if (updateIntervalField != null)
            {
                float currentInterval = (float)updateIntervalField.GetValue(wallOptimizer);
                updateIntervalField.SetValue(wallOptimizer, Mathf.Max(currentInterval, 0.5f));
            }
            
            // Ограничиваем количество обрабатываемых стен за один кадр
            var maxWallsPerFrameField = wallOptimizer.GetType().GetField("maxWallsPerFrame");
            if (maxWallsPerFrameField != null)
            {
                int currentMaxWalls = (int)maxWallsPerFrameField.GetValue(wallOptimizer);
                maxWallsPerFrameField.SetValue(wallOptimizer, Mathf.Min(currentMaxWalls, 5));
            }
            
            // Отключаем отладочную информацию
            var showDebugInfoField = wallOptimizer.GetType().GetField("showDebugInfo");
            if (showDebugInfoField != null)
            {
                showDebugInfoField.SetValue(wallOptimizer, false);
            }
        }
    }
    
    /// <summary>
    /// Проверка на дубликаты компонентов в сцене
    /// </summary>
    public void CheckForDuplicates()
    {
        // Проверяем наличие дубликатов AR Session
        ARSession[] sessions = FindObjectsOfType<ARSession>();
        if (sessions.Length > 1)
        {
            Debug.LogWarning($"Найдено {sessions.Length} компонентов ARSession. Это может вызывать проблемы.");
        }
        
        // Проверяем наличие дубликатов AR Session Origin
        ARSessionOrigin[] origins = FindObjectsOfType<ARSessionOrigin>();
        if (origins.Length > 1)
        {
            Debug.LogWarning($"Найдено {origins.Length} компонентов ARSessionOrigin. Это может вызывать проблемы.");
        }
        
        // Проверяем наличие дубликатов WallOptimizer
        WallOptimizer[] optimizers = FindObjectsOfType<WallOptimizer>();
        if (optimizers.Length > 1)
        {
            Debug.LogWarning($"Найдено {optimizers.Length} компонентов WallOptimizer. Это может вызывать проблемы.");
        }
    }
    
    /// <summary>
    /// Отображение отладочной информации в режиме редактора
    /// </summary>
    private void OnDrawGizmos()
    {
        #if UNITY_EDITOR
        // Отображаем линии между связанными компонентами для наглядности
        if (arSessionOrigin != null && wallOptimizer != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(arSessionOrigin.transform.position, wallOptimizer.transform.position);
        }
        
        if (arSessionOrigin != null && uiCanvas != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(arSessionOrigin.transform.position, uiCanvas.transform.position);
        }
        #endif
    }
} 