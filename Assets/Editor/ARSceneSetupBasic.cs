using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System.IO;
using UnityEditor.SceneManagement;
using Unity.XR.CoreUtils;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.XR;

namespace ARSceneSetupUtils
{
public class ARSceneSetupBasic : EditorWindow
{
    // Helper method to avoid SendMessage warnings by deferring component addition
    private static T SafeAddComponent<T>(GameObject target) where T : Component
    {
        // Check if component already exists
        T existing = target.GetComponent<T>();
        if (existing != null)
            return existing;
            
        // Создаем компонент немедленно для возврата
        T component = target.AddComponent<T>();
        
        // Проверяем, нужно ли удалить компонент в следующем кадре
        // для избежания предупреждений SendMessage
        bool removeComponent = false;
        
        // Пробуем добавить еще один компонент - это вызовет ошибку, если компонент уникальный
        try 
        {
            // Это вызовет ошибку, если компонент должен быть единственным
            var testComponent = target.AddComponent<T>();
            
            // Если ошибки не было, то тестовый компонент нужно удалить
            UnityEngine.Object.DestroyImmediate(testComponent);
        }
        catch 
        {
            // Ошибка означает, что компонент должен быть единственным
            // т.е. мы уже его добавили, и не нужно пытаться добавлять повторно
            removeComponent = true;
        }
        
        // Если не нужно удалять компонент, то планируем добавление через EditorApplication.delayCall
        if (!removeComponent)
        {
            // Queue the component addition for next editor update to avoid SendMessage warnings
            EditorApplication.delayCall += () => 
            {
                if (target != null) // Check if target still exists
                {
                    // Проверяем, не добавлен ли уже компонент
                    T existingDelayed = target.GetComponent<T>();
                    
                    if (existingDelayed == null || existingDelayed == component)
                    {
                        try
                        {
                            // Добавляем компонент только если он еще не существует
                            if (existingDelayed == null)
                            {
                                target.AddComponent<T>();
                            }
                            
                            EditorUtility.SetDirty(target);
                        }
                        catch (System.Exception e)
                        {
                            // Игнорируем ошибки, если компонент уже существует или его нельзя добавлять повторно
                            Debug.LogWarning($"SafeAddComponent: Не удалось добавить компонент {typeof(T).Name}: {e.Message}");
                        }
                    }
                }
            };
        }
        
        return component;
    }
    
        /// <summary>
        /// Checks if AR packages are installed in the project
        /// </summary>
        private static void CheckARPackages()
    {
            bool hasArFoundation = false;
            bool hasArSubsystems = false;
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name.Contains("Unity.XR.ARFoundation"))
                    hasArFoundation = true;
                
                if (assembly.GetName().Name.Contains("Unity.XR.ARSubsystems"))
                    hasArSubsystems = true;
            }
            
            if (!hasArFoundation || !hasArSubsystems)
            {
                EditorUtility.DisplayDialog("AR Packages Missing", 
                    "AR Foundation and XR Subsystems packages are required.\nPlease install them via Package Manager.", "OK");
                
                throw new System.Exception("AR Foundation packages are not installed.");
            }
            
            Debug.Log("AR Foundation packages check passed successfully.");
        }
        
        [MenuItem("AR/Setup AR Scene (Basic)")]
        public static void SetupARScene()
        {
            // First, check if the AR features are imported in the project
            CheckARPackages();
        
            // Attempt to load an existing AR scene first
            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (!currentScene.IsValid() || !currentScene.isLoaded)
            {
                Debug.LogError("No active scene found. Please open a scene first.");
                return;
            }
            
            if (EditorUtility.DisplayDialog("Setup AR Scene", 
                "This will setup a basic AR scene with the necessary components.\n\n" +
                "Camera background will be rendered natively using ARCameraBackground.\n\n" +
                "Do you want to continue?", "Yes", "Cancel"))
        {
                // Save current scene if it has unsaved changes
                if (currentScene.isDirty)
                {
                    if (EditorUtility.DisplayDialog("Save Current Scene", 
                        "Do you want to save the current scene before setting up AR?", "Save", "Continue Without Saving"))
                    {
                        EditorSceneManager.SaveScene(currentScene);
                    }
                }
                
                GameObject xrSessionOrigin = null;
                XROrigin xrOrigin = null;
                
                // Create AR systems
                XROrigin existingXROrigin = FindFirstObjectByType<XROrigin>();
                if (existingXROrigin == null)
                {
                    xrOrigin = SetupARSystem();
                    
                    if (xrOrigin != null)
                    {
                        xrSessionOrigin = xrOrigin.gameObject;
                    }
        }
        else
        {
                    xrOrigin = existingXROrigin;
                    xrSessionOrigin = existingXROrigin.gameObject;
                    Debug.Log("Using existing XR Origin in the scene");
                }
                
                if (xrOrigin == null || xrSessionOrigin == null)
                {
                    Debug.LogError("Failed to setup XR Origin");
                    return;
                }
                
                // Create UI
                GameObject canvasObj = SetupUI();
                
                // Create light if needed
                CreateLight();
                
                // Create AR Segmentation Manager
                CreateSegmentationManager();
                
                // Create ML Manager Adapter
                CreateMLManagerAdapter();
                
                // Create Wall Painter Controller
                CreateWallPainterController(xrOrigin);
                
                // Create AR Plane Visualizer
                CreateARPlaneVisualizer(xrOrigin);
                
                // Создаем контроллер для обеспечения обнаружения вертикальных плоскостей при запуске
                GameObject planeDetectionControllerObj = new GameObject("AR Plane Detection Controller");
                var planeDetectionController = planeDetectionControllerObj.AddComponent<ARPlaneDetectionController>();
                
                // Теперь находим и назначаем ARPlaneManager на компонент, чтобы не требовалось ручное назначение
                if (xrOrigin != null)
                {
                    ARPlaneManager planeManager = xrOrigin.GetComponent<ARPlaneManager>();
                    if (planeManager == null)
                    {
                        // Если нет на XR Origin, создаем и добавляем
                        planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                        Debug.Log("Added ARPlaneManager to XR Origin");
                        
                        // Настраиваем для обнаружения обоих типов плоскостей
                        #if UNITY_2020_1_OR_NEWER
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                        #else
                        planeManager.detectionMode = PlaneDetectionFlags.Horizontal | PlaneDetectionFlags.Vertical;
                        #endif
                    }
                    
                    // Напрямую назначаем ссылки через публичные свойства, если они есть
                    var planeManagerProperty = typeof(ARPlaneDetectionController).GetProperty("PlaneManager", 
                                    System.Reflection.BindingFlags.Public | 
                                    System.Reflection.BindingFlags.Instance);
                    
                    if (planeManagerProperty != null && planeManagerProperty.CanWrite)
                    {
                        // Используем публичный сеттер, если он есть
                        planeManagerProperty.SetValue(planeDetectionController, planeManager);
                        Debug.Log("ARPlaneManager reference set on ARPlaneDetectionController via public property");
                    }
                    else
                    {
                        // Добавим публичный метод для установки менеджера плоскостей в ARPlaneDetectionController
                        var setManagerMethod = typeof(ARPlaneDetectionController).GetMethod("SetPlaneManager", 
                                              System.Reflection.BindingFlags.Public | 
                                              System.Reflection.BindingFlags.Instance);
                        
                        if (setManagerMethod != null)
                        {
                            setManagerMethod.Invoke(planeDetectionController, new object[] { planeManager });
                            Debug.Log("ARPlaneManager reference set on ARPlaneDetectionController via SetPlaneManager method");
                        }
                        else
                        {
                            // Последний вариант - через приватное поле
                            var field = typeof(ARPlaneDetectionController).GetField("planeManager", 
                                            System.Reflection.BindingFlags.Instance | 
                                            System.Reflection.BindingFlags.NonPublic);
                            
                            if (field != null)
                            {
                                field.SetValue(planeDetectionController, planeManager);
                                Debug.Log("ARPlaneManager reference set on ARPlaneDetectionController via private field");
                            }
                            else
                            {
                                Debug.LogWarning("ARPlaneDetectionController не удалось автоматически установить planeManager. Пожалуйста, назначьте его вручную в инспекторе.");
                            }
                        }
                    }
                    
                    // Создаем также обработчик событий для плоскостей во время выполнения
                    // Это обеспечит корректную обработку и визуализацию вертикальных плоскостей в рантайме
                    GameObject runtimeHandlerObj = new GameObject("Runtime AR Plane Events Handler");
                    try
                    {
                        var runtimeHandler = runtimeHandlerObj.AddComponent<RuntimeARPlaneEventsHandler>();
                        
                        // Напрямую назначаем ссылки через публичные методы или свойства, если они есть
                        var setPlaneManagerMethod = typeof(RuntimeARPlaneEventsHandler).GetMethod("SetPlaneManager", 
                                              System.Reflection.BindingFlags.Public | 
                                              System.Reflection.BindingFlags.Instance);
                        
                        if (setPlaneManagerMethod != null)
                        {
                            setPlaneManagerMethod.Invoke(runtimeHandler, new object[] { planeManager });
                            Debug.Log("ARPlaneManager reference set on RuntimeARPlaneEventsHandler via SetPlaneManager method");
                        }
                        else
                        {
                            // Пытаемся через публичное свойство
                            var runtimePlaneManagerProperty = typeof(RuntimeARPlaneEventsHandler).GetProperty("PlaneManager", 
                                            System.Reflection.BindingFlags.Public | 
                                            System.Reflection.BindingFlags.Instance);
                            
                            if (runtimePlaneManagerProperty != null && runtimePlaneManagerProperty.CanWrite)
                            {
                                runtimePlaneManagerProperty.SetValue(runtimeHandler, planeManager);
                                Debug.Log("ARPlaneManager reference set on RuntimeARPlaneEventsHandler via public property");
                            }
                            else
                            {
                                // Последний вариант - через приватное поле
                                var runtimeField = typeof(RuntimeARPlaneEventsHandler).GetField("planeManager", 
                                                System.Reflection.BindingFlags.Instance | 
                                                System.Reflection.BindingFlags.NonPublic);
                                
                                if (runtimeField != null)
                                {
                                    runtimeField.SetValue(runtimeHandler, planeManager);
                                    Debug.Log("ARPlaneManager reference set on RuntimeARPlaneEventsHandler via private field");
                                }
                            }
                        }
                        
                        // Если в сцене есть AR Plane Visualizer, назначаем его Trackables
                        GameObject visualizer = GameObject.Find("AR Plane Visualizer");
                        if (visualizer != null)
                        {
                            Transform trackables = visualizer.transform.Find("Trackables");
                            if (trackables != null)
                            {
                                // Пытаемся через публичный метод
                                var setTrackablesParentMethod = typeof(RuntimeARPlaneEventsHandler).GetMethod("SetTrackablesParent", 
                                                      System.Reflection.BindingFlags.Public | 
                                                      System.Reflection.BindingFlags.Instance);
                                
                                if (setTrackablesParentMethod != null)
                                {
                                    setTrackablesParentMethod.Invoke(runtimeHandler, new object[] { trackables });
                                    Debug.Log("Set Trackables parent on RuntimeARPlaneEventsHandler via SetTrackablesParent method");
                                }
                                else
                                {
                                    // Пытаемся через публичное свойство
                                    var trackablesProperty = typeof(RuntimeARPlaneEventsHandler).GetProperty("CustomTrackablesParent", 
                                                    System.Reflection.BindingFlags.Public | 
                                                    System.Reflection.BindingFlags.Instance);
                                    
                                    if (trackablesProperty != null && trackablesProperty.CanWrite)
                                    {
                                        trackablesProperty.SetValue(runtimeHandler, trackables);
                                        Debug.Log("Set Trackables parent on RuntimeARPlaneEventsHandler via public property");
                                    }
                                    else
                                    {
                                        // Последний вариант - через приватное поле
                                        var trackablesField = typeof(RuntimeARPlaneEventsHandler).GetField("customTrackablesParent", 
                                                        System.Reflection.BindingFlags.Instance | 
                                                        System.Reflection.BindingFlags.NonPublic);
                                        
                                        if (trackablesField != null)
                                        {
                                            trackablesField.SetValue(runtimeHandler, trackables);
                                            Debug.Log("Set Trackables parent on RuntimeARPlaneEventsHandler via private field");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Не удалось добавить RuntimeARPlaneEventsHandler: {ex.Message}. Добавьте его вручную в сцену для корректной работы с вертикальными плоскостями.");
                        GameObject.DestroyImmediate(runtimeHandlerObj);
                    }
                }
                
                Debug.Log("Added AR Plane Detection Controller to ensure vertical plane detection");
                
                Selection.activeGameObject = xrSessionOrigin;
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                
                Debug.Log("AR Scene setup completed successfully");
                EditorUtility.DisplayDialog("Setup Complete", 
                    "AR Scene has been set up successfully!\n\n" +
                    "AR camera feed is configured to render natively with ARCameraBackground.", "OK");
        }
    }
    
    /// <summary>
    /// Создает новую сцену для AR
    /// </summary>
    private static Scene CreateARScene()
    {
        // Создаем новую пустую сцену
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // Добавляем основной свет
        GameObject directionalLight = new GameObject("Directional Light");
        Light light = SafeAddComponent<Light>(directionalLight);
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(1.0f, 0.95f, 0.84f);
        directionalLight.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        return newScene;
    }
    
    public static XROrigin SetupARSystem()
    {
        try
        {
            // Проверяем и удаляем дублирующиеся ARSession компоненты
            ARSession[] arSessions = FindObjectsByType<ARSession>(FindObjectsSortMode.None);
            if (arSessions.Length > 1)
            {
                Debug.Log($"Found {arSessions.Length} ARSession components, removing duplicates and keeping only one.");
                // Оставляем первый ARSession, остальные отключаем
                for (int i = 1; i < arSessions.Length; i++)
                {
                    GameObject sessionObj = arSessions[i].gameObject;
                    Debug.Log($"Disabling duplicate ARSession on {sessionObj.name}");
                    arSessions[i].enabled = false;
                }
            }
            
            // Удаляем все старые ARMeshManager, чтобы не плодить дубли
            foreach (var old in UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None))
                GameObject.DestroyImmediate(old.gameObject);
            
            // Проверяем существование необходимых компонентов
                XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
                UnityEngine.XR.ARFoundation.ARSession arSession = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            
            // Создаем AR Session если его нет
            if (arSession == null)
            {
                GameObject arSessionObj = new GameObject("AR Session");
                arSession = SafeAddComponent<UnityEngine.XR.ARFoundation.ARSession>(arSessionObj);
                SafeAddComponent<UnityEngine.XR.ARFoundation.ARInputManager>(arSessionObj);
                
                Debug.Log("Created AR Session");
            }
            
            // Создаем XR Origin если его нет
            GameObject xrOriginObj = null;
            if (xrOrigin == null)
            {
                // Создаем объект XR Origin
                xrOriginObj = new GameObject("XR Origin");
                xrOrigin = SafeAddComponent<XROrigin>(xrOriginObj);
                
                // Настраиваем Camera Offset
                GameObject cameraOffsetObj = new GameObject("Camera Offset");
                cameraOffsetObj.transform.SetParent(xrOriginObj.transform);
                
                // Создаем AR Camera
                GameObject arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffsetObj.transform);
                
                // Настраиваем камеру
                Camera arCamera = SafeAddComponent<Camera>(arCameraObj);
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                
                // Добавляем компоненты AR к камере
                    ARCameraManager cameraManager = SafeAddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(arCameraObj);
                    ARCameraBackground cameraBackground = SafeAddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>(arCameraObj);
                    
                    // Добавляем TrackedPoseDriver для решения проблемы с позиционированием камеры
                    TrackedPoseDriver poseDriver = SafeAddComponent<TrackedPoseDriver>(arCameraObj);
                    
                    // Настраиваем автофокус для мобильных устройств
                    #if UNITY_IOS || UNITY_ANDROID
                    cameraManager.autoFocusRequested = true;
                    #endif
                
                // Настраиваем XROrigin
                xrOrigin.Camera = arCamera;
                xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
                
                    Debug.Log("Created XR Origin with AR Camera (including ARCameraBackground for native rendering)");
            }
            else
            {
                xrOriginObj = xrOrigin.gameObject;
                    
                    // Проверяем наличие необходимых компонентов у существующей камеры
                    if (xrOrigin.Camera != null)
                    {
                        GameObject cameraObj = xrOrigin.Camera.gameObject;
                        
                        // Добавляем ARCameraManager если нет
                        if (cameraObj.GetComponent<ARCameraManager>() == null)
                        {
                            ARCameraManager cameraManager = SafeAddComponent<ARCameraManager>(cameraObj);
                            #if UNITY_IOS || UNITY_ANDROID
                            cameraManager.autoFocusRequested = true;
                            #endif
                            Debug.Log("Added ARCameraManager to existing AR Camera");
                        }
                        
                        // Добавляем ARCameraBackground если нет
                        if (cameraObj.GetComponent<ARCameraBackground>() == null)
                        {
                            SafeAddComponent<ARCameraBackground>(cameraObj);
                            Debug.Log("Added ARCameraBackground to existing AR Camera for native rendering");
                        }
                        
                        // Добавляем TrackedPoseDriver если нет
                        if (cameraObj.GetComponent<TrackedPoseDriver>() == null)
                        {
                            TrackedPoseDriver poseDriver = SafeAddComponent<TrackedPoseDriver>(cameraObj);
                            Debug.Log("Added TrackedPoseDriver to existing AR Camera");
                        }
                    }
            }
            
            // Строго привязываем ARMeshManager к только что созданному XR Origin
            var meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            meshManagerObj.transform.localPosition = Vector3.zero;
            meshManagerObj.transform.localRotation = Quaternion.identity;
            meshManagerObj.transform.localScale = Vector3.one;
            ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
            meshManager.density = 0.5f;
            
            Debug.Log("Created AR Mesh Manager as child of XR Origin");
            
            // Добавляем недостающие компоненты для работы с AR
            if (xrOrigin != null && xrOrigin.gameObject.GetComponent<ARRaycastManager>() == null)
            {
                SafeAddComponent<ARRaycastManager>(xrOrigin.gameObject);
                Debug.Log("Added AR Raycast Manager to XR Origin");
            }
            
            // Добавляем ARPlaneManager с поддержкой горизонтальных и вертикальных плоскостей
            if (xrOrigin != null && xrOrigin.gameObject.GetComponent<ARPlaneManager>() == null)
            {
                ARPlaneManager planeManager = SafeAddComponent<ARPlaneManager>(xrOrigin.gameObject);
                
                // Включаем обнаружение как горизонтальных, так и вертикальных плоскостей
                #if UNITY_2020_1_OR_NEWER
                planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal | 
                                                     UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
                #else
                planeManager.detectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionFlags.Horizontal | 
                                            UnityEngine.XR.ARSubsystems.PlaneDetectionFlags.Vertical;
                #endif
                
                // Назначаем префаб плоскостей
                SetupPlanePrefab(planeManager);
                
                Debug.Log("Added AR Plane Manager with horizontal and vertical plane detection to XR Origin");
            }
            // Если ARPlaneManager уже существует, настраиваем его для обнаружения вертикальных плоскостей
            else if (xrOrigin != null)
            {
                ARPlaneManager existingPlaneManager = xrOrigin.gameObject.GetComponent<ARPlaneManager>();
                if (existingPlaneManager != null)
                {
                    #if UNITY_2020_1_OR_NEWER
                    existingPlaneManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal | 
                                                                UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
                    #else
                    existingPlaneManager.detectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionFlags.Horizontal | 
                                                       UnityEngine.XR.ARSubsystems.PlaneDetectionFlags.Vertical;
                    #endif
                    
                    // Проверяем и назначаем префаб плоскостей, если не задан
                    if (existingPlaneManager.planePrefab == null)
                    {
                        SetupPlanePrefab(existingPlaneManager);
                    }
                    
                    Debug.Log("Updated existing AR Plane Manager to detect both horizontal and vertical planes");
                }
            }
            
            Debug.Log("AR System setup completed successfully");
                Debug.Log("Note: AR camera feed will be rendered natively with ARCameraBackground");
            
            return xrOrigin;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up AR System: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Creates the AR components for the scene
    /// </summary>
    /// <returns>The XR Origin GameObject</returns>
    private static GameObject CreateARComponents()
    {
        try
        {
            // Create a parent object for AR system
            GameObject arSystem = new GameObject("AR System");
            
            // Setup the AR system and получаем XROrigin
            XROrigin xrOrigin = SetupARSystem();
            
            // Обязательно вкладываем XR Origin и AR Session в arSystem
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetParent(arSystem.transform, false);
                Debug.Log("XR Origin added to AR System container");
            }
            
            // Найдём ARSession и тоже прицепим
                var arSession = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            if (arSession != null)
            {
                arSession.transform.SetParent(arSystem.transform, false);
                Debug.Log("AR Session added to AR System container");
            }
            
            // Добавляем автоматический исправитель иерархии при запуске
            if (xrOrigin != null)
            {
                CreateARMeshManagerChecker(xrOrigin);
            }
            
            // Return the container system
            return arSystem;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error creating AR components: {ex.Message}\n{ex.StackTrace}");
            return new GameObject("AR System (Error)");
        }
    }
    
    // Create color palette buttons
    private static void CreateColorButtons(Transform parent)
    {
        GameObject colorPalette = new GameObject("Color Palette");
        colorPalette.transform.SetParent(parent, false);
        
        RectTransform rect = colorPalette.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.6f, 0.1f);
        rect.anchorMax = new Vector2(0.95f, 0.9f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        
        // Define colors for the palette
        Color[] colors = new Color[]
        {
            new Color(1f, 1f, 1f, 1f),      // White
            new Color(0.8f, 0.8f, 0.8f, 1f), // Light gray
            new Color(0.5f, 0.8f, 1f, 1f),   // Light blue
            new Color(0.2f, 0.6f, 1f, 1f),   // Blue
            new Color(0.8f, 0.5f, 1f, 1f),   // Purple
            new Color(1f, 0.5f, 0.5f, 1f),   // Pink
            new Color(1f, 0.8f, 0.2f, 1f),   // Yellow
            new Color(0.5f, 0.8f, 0.2f, 1f)  // Green
        };
        
        // Calculate grid layout
        int columns = 4;
        int rows = (colors.Length + columns - 1) / columns;
        float buttonWidth = 1f / columns;
        float buttonHeight = 1f / rows;
        
        // Create color buttons
        for (int i = 0; i < colors.Length; i++)
        {
            int row = i / columns;
            int col = i % columns;
            
            // Calculate anchor positions
            Vector2 anchorMin = new Vector2(col * buttonWidth, 1f - (row + 1) * buttonHeight);
            Vector2 anchorMax = new Vector2((col + 1) * buttonWidth, 1f - row * buttonHeight);
            
            GameObject colorButton = new GameObject($"Color_{i}");
            colorButton.transform.SetParent(colorPalette.transform, false);
            
            RectTransform buttonRect = colorButton.AddComponent<RectTransform>();
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            buttonRect.offsetMin = new Vector2(2, 2);
            buttonRect.offsetMax = new Vector2(-2, -2);
            
            Image buttonImage = colorButton.AddComponent<Image>();
            buttonImage.color = colors[i];
            
            Button button = colorButton.AddComponent<Button>();
            
            // Store selected color in button's name (will be parsed in the component references setup)
            colorButton.name = $"ColorButton_{colors[i].r}_{colors[i].g}_{colors[i].b}";
        }
    }
    
    private static GameObject SetupUI()
    {
        try {
                // Get AR Camera reference
                Camera arCamera = null;
                XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null)
                {
                    arCamera = xrOrigin.Camera;
                }
                
                if (arCamera == null)
                {
                    Debug.LogWarning("Could not find AR Camera. Canvas will be set to ScreenSpaceOverlay mode.");
                }
                
            // Create Canvas for UI
            GameObject canvasObj = new GameObject("UI Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
                
                // Set camera space rendering if AR camera is available
                if (arCamera != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = arCamera;
                    canvas.planeDistance = 1.0f;
                    Debug.Log("Canvas set to Camera Space rendering mode with AR Camera");
                }
                else
                {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }
                
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // Создаем панель с цветовой палитрой в нижней части экрана
            GameObject colorPanelObj = new GameObject("Color Panel");
            colorPanelObj.transform.SetParent(canvasObj.transform, false);
            
            RectTransform colorPanelRect = colorPanelObj.AddComponent<RectTransform>();
            colorPanelRect.anchorMin = new Vector2(0, 0);
            colorPanelRect.anchorMax = new Vector2(1, 0.1f);
            colorPanelRect.offsetMin = new Vector2(10, 10);
            colorPanelRect.offsetMax = new Vector2(-10, -10);
            
            Image colorPanelImage = colorPanelObj.AddComponent<Image>();
            colorPanelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            
            // Создаем цветовую палитру
            CreateColorButtons(colorPanelObj.transform);
            
            return canvasObj;
        }
        catch (Exception e) {
            Debug.LogError($"Error setting up UI: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Creates the AR UI Canvas with main UI elements
    /// </summary>
    /// <returns>The Canvas GameObject</returns>
    private static GameObject CreateARUICanvas()
    {
        // Create the main UI system with only color palette
        GameObject uiCanvas = SetupUI();
        
        return uiCanvas;
    }
    
    /// <summary>
    /// Проверяет и исправляет положение ARMeshManager в иерархии объектов
    /// </summary>
    private static void FixARMeshManagerHierarchy()
    {
        try
        {
            Debug.Log("Начинаю проверку иерархии ARMeshManager...");
            
            // Сначала получаем XROrigin
                XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("ОШИБКА: XROrigin не найден в сцене! Невозможно исправить иерархию.");
                return;
            }
            
            // Получаем все ARMeshManager в сцене, которые НЕ являются детьми XROrigin
            System.Collections.Generic.List<ARMeshManager> invalidManagers = new System.Collections.Generic.List<ARMeshManager>();
            
            ARMeshManager[] allManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None);
            foreach (ARMeshManager meshManager in allManagers)
            {
                if (meshManager != null && meshManager.transform.parent != xrOrigin.transform)
                {
                    invalidManagers.Add(meshManager);
                    
                    // Если ARMeshManager уже создан, пытаемся его переместить вместо удаления
                    // Это поможет избежать потери ссылок
                    try 
                    {
                        string originalName = meshManager.gameObject.name;
                        Debug.LogWarning($"Перемещаем ARMeshManager {originalName} под XROrigin вместо удаления");
                        
                        meshManager.transform.SetParent(xrOrigin.transform);
                        meshManager.transform.localPosition = Vector3.zero;
                        meshManager.transform.localRotation = Quaternion.identity;
                        meshManager.transform.localScale = Vector3.one;
                        
                        // Удаляем его из списка недействительных, так как мы его исправили
                        invalidManagers.Remove(meshManager);
                        
                        Debug.Log($"ARMeshManager {originalName} успешно перемещен под XROrigin");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Не удалось переместить ARMeshManager: {e.Message}. Будет создан новый.");
                    }
                }
            }
            
            int invalidCount = invalidManagers.Count;
            if (invalidCount == 0)
            {
                // Проверяем, есть ли вообще ARMeshManager в сцене
                ARMeshManager existingManager = xrOrigin.GetComponentInChildren<ARMeshManager>(true);
                bool hasMeshManager = existingManager != null;
                
                if (!hasMeshManager)
                {
                    Debug.Log("ARMeshManager не найден. Создаем новый как дочерний объект XROrigin.");
                    
                    // Создаем новый ARMeshManager под XROrigin
                    GameObject meshManagerObj = new GameObject("AR Mesh Manager");
                    meshManagerObj.transform.SetParent(xrOrigin.transform, false);
                    meshManagerObj.transform.localPosition = Vector3.zero;
                    meshManagerObj.transform.localRotation = Quaternion.identity;
                    meshManagerObj.transform.localScale = Vector3.one;
                    
                    ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
                    meshManager.density = 0.5f;
                }
                else
                {
                    Debug.Log("Все ARMeshManager уже имеют правильную иерархию.");
                }
                
                return;
            }
            
            Debug.LogWarning($"Найдено {invalidCount} ARMeshManager с неправильной иерархией, которые не удалось исправить.");
            
            // Удаляем все неправильные ARMeshManager, которые не удалось исправить
            foreach (ARMeshManager manager in invalidManagers)
            {
                Debug.LogWarning($"Удаляем ARMeshManager с неправильной иерархией: {manager.gameObject.name}");
                GameObject.DestroyImmediate(manager.gameObject);
            }
            
            // Создаем новый ARMeshManager под XROrigin
            GameObject newMeshManagerObj = new GameObject("AR Mesh Manager");
            newMeshManagerObj.transform.SetParent(xrOrigin.transform, false);
            newMeshManagerObj.transform.localPosition = Vector3.zero;
            newMeshManagerObj.transform.localRotation = Quaternion.identity;
            newMeshManagerObj.transform.localScale = Vector3.one;
            
            ARMeshManager newMeshManager = SafeAddComponent<ARMeshManager>(newMeshManagerObj);
            newMeshManager.density = 0.5f;
            
            Debug.Log("Создан новый ARMeshManager как дочерний объект XROrigin");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Ошибка при исправлении иерархии ARMeshManager: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Создает скрипт автоматической проверки ARMeshManager при запуске сцены
    /// </summary>
    private static void CreateARMeshManagerChecker(XROrigin xrOrigin)
    {
        // В режиме редактора не создаем объект с DontDestroyOnLoad,
        // вместо этого добавляем компонент BasicARMeshManagerCheckerStarter
        
        // Проверяем, есть ли уже стартер в сцене
            BasicARMeshManagerCheckerStarter existingStarter = FindFirstObjectByType<BasicARMeshManagerCheckerStarter>();
        if (existingStarter != null)
        {
            Debug.Log("BasicARMeshManagerCheckerStarter уже существует в сцене");
            
            // Обновляем ссылку на XROrigin если она нулевая
            if (existingStarter.targetXROrigin == null)
            {
                existingStarter.targetXROrigin = xrOrigin;
                Debug.Log("Обновлена ссылка на XROrigin в существующем BasicARMeshManagerCheckerStarter");
            }
            return;
        }
        
        // Создаем объект для запуска проверки во время игры
        GameObject starterObj = new GameObject("AR Mesh Manager Checker Starter");
        BasicARMeshManagerCheckerStarter starter = starterObj.AddComponent<BasicARMeshManagerCheckerStarter>();
        if (starter != null && xrOrigin != null)
        {
            starter.targetXROrigin = xrOrigin;
        }
        
        Debug.Log("BasicARMeshManagerCheckerStarter добавлен для создания валидатора при запуске игры");
    }
        
        /// <summary>
        /// Creates a directional light if there isn't one in the scene
        /// </summary>
        private static void CreateLight()
        {
            // Check if a directional light already exists
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            bool hasDirectionalLight = false;
            
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    hasDirectionalLight = true;
                    break;
                }
            }
            
            if (!hasDirectionalLight)
            {
                GameObject lightObj = new GameObject("Directional Light");
                Light light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                light.color = new Color(1.0f, 0.95f, 0.84f);
                lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
                
                Debug.Log("Created directional light for the scene");
            }
        }
        
        /// <summary>
        /// Creates a SegmentationManager if it doesn't exist in the scene
        /// </summary>
        private static void CreateSegmentationManager()
        {
            try
            {
                // Check if SegmentationManager already exists
                var existingManager = FindFirstObjectByType<SegmentationManager>();
                if (existingManager != null)
                {
                    Debug.Log("Using existing SegmentationManager");
                    
                    // Check if model asset is assigned, if not - try to assign it
                    var modelField = typeof(SegmentationManager).GetField("ModelAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (modelField != null && modelField.GetValue(existingManager) == null)
                    {
                        // Find and assign model.onnx
                        var modelAsset = AssetDatabase.LoadAssetAtPath<Unity.Barracuda.NNModel>("Assets/ML/Models/model.onnx");
                        if (modelAsset != null)
                        {
                            modelField.SetValue(existingManager, modelAsset);
                            Debug.Log("Assigned model.onnx to existing SegmentationManager");
                            EditorUtility.SetDirty(existingManager);
                        }
                        else
                        {
                            Debug.LogWarning("Could not find model.onnx at Assets/ML/Models/model.onnx");
                        }
                    }
                    
                    // Настройка параметров существующего SegmentationManager
                    try 
                    {
                        // Установка правильных входных и выходных параметров для модели
                        var inputWidthField = typeof(SegmentationManager).GetField("inputWidth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var inputHeightField = typeof(SegmentationManager).GetField("inputHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var outputNameField = typeof(SegmentationManager).GetField("outputName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var wallClassIdField = typeof(SegmentationManager).GetField("wallClassId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var debugModeField = typeof(SegmentationManager).GetField("debugMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (inputWidthField != null && inputHeightField != null)
                        {
                            inputWidthField.SetValue(existingManager, 268);
                            inputHeightField.SetValue(existingManager, 412);
                            Debug.Log("Set input dimensions to 268x412 for existing SegmentationManager");
                        }
                        
                        if (outputNameField != null)
                        {
                            outputNameField.SetValue(existingManager, "logits");
                            Debug.Log("Set output name to 'logits' for existing SegmentationManager");
                        }
                        
                        if (wallClassIdField != null)
                        {
                            wallClassIdField.SetValue(existingManager, 9);
                            Debug.Log("Set wall class ID to 9 for existing SegmentationManager");
                        }
                        
                        if (debugModeField != null)
                        {
                            debugModeField.SetValue(existingManager, true);
                            Debug.Log("Enabled debug mode for existing SegmentationManager");
                        }
                        
                        EditorUtility.SetDirty(existingManager);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error configuring existing SegmentationManager: {ex.Message}");
                    }
                    
                    // Проверяем наличие компонента ModelFixer на объекте SegmentationManager
                    var existingFixer = existingManager.GetComponent<ModelFixer>();
                    if (existingFixer == null)
                    {
                        // Добавляем ModelFixer если его еще нет
                        try 
                        {
                            // Проверяем, существует ли тип ModelFixer
                            Type modelFixerType = Type.GetType("ModelFixer, Assembly-CSharp");
                            if (modelFixerType != null)
                            {
                                // Добавляем ModelFixer к существующему объекту SegmentationManager
                                var modelFixer = existingManager.gameObject.AddComponent(modelFixerType);
                                
                                // Устанавливаем ссылку на SegmentationManager
                                var segManagerField = modelFixerType.GetField("segmentationManager");
                                if (segManagerField != null)
                                {
                                    segManagerField.SetValue(modelFixer, existingManager);
                                    Debug.Log("Added ModelFixer to existing SegmentationManager for automatic configuration");
                                }
                            }
                            else
                            {
                                // Если не найден через Type.GetType, пробуем прямое добавление
                                try 
                                {
                                    var modelFixer = existingManager.gameObject.AddComponent<ModelFixer>();
                                    if (modelFixer != null)
                                    {
                                        modelFixer.segmentationManager = existingManager;
                                        Debug.Log("Added ModelFixer to existing SegmentationManager for automatic configuration");
                                    }
                                }
                                catch (Exception)
                                {
                                    Debug.LogWarning("Could not add ModelFixer - type not found. The SegmentationManager may require manual configuration.");
                                }
                            }
                        }
                        catch (Exception fixerEx)
                        {
                            Debug.LogWarning($"Error adding ModelFixer to existing SegmentationManager: {fixerEx.Message}");
                        }
                    }
                    else
                    {
                        Debug.Log("ModelFixer already attached to existing SegmentationManager");
                    }
                    
                    return;
                }
                
                // Create a new SegmentationManager
                GameObject segManagerObj = new GameObject("Segmentation Manager");
                
                // Add the component if the type exists
                Type segManagerType = Type.GetType("SegmentationManager, Assembly-CSharp");
                if (segManagerType != null)
                {
                    var segManager = segManagerObj.AddComponent(segManagerType);
                    Debug.Log("Created Segmentation Manager");
                    
                    // Find and assign model.onnx
                    var modelAsset = AssetDatabase.LoadAssetAtPath<Unity.Barracuda.NNModel>("Assets/ML/Models/model.onnx");
                    if (modelAsset != null)
                    {
                        // Use reflection to access and set the ModelAsset field
                        var modelField = segManagerType.GetField("ModelAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (modelField != null)
                        {
                            modelField.SetValue(segManager, modelAsset);
                            Debug.Log("Assigned model.onnx to new SegmentationManager");
                            
                            // Установка правильных входных и выходных параметров для модели
                            var inputWidthField = segManagerType.GetField("inputWidth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var inputHeightField = segManagerType.GetField("inputHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var outputNameField = segManagerType.GetField("outputName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var wallClassIdField = segManagerType.GetField("wallClassId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var debugModeField = segManagerType.GetField("debugMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (inputWidthField != null && inputHeightField != null)
                            {
                                inputWidthField.SetValue(segManager, 268);
                                inputHeightField.SetValue(segManager, 412);
                                Debug.Log("Set input dimensions to 268x412 for SegmentationManager");
                            }
                            
                            if (outputNameField != null)
                            {
                                outputNameField.SetValue(segManager, "logits");
                                Debug.Log("Set output name to 'logits' for SegmentationManager");
                            }
                            
                            if (wallClassIdField != null)
                            {
                                wallClassIdField.SetValue(segManager, 9);
                                Debug.Log("Set wall class ID to 9 for SegmentationManager");
                            }
                            
                            if (debugModeField != null)
                            {
                                debugModeField.SetValue(segManager, true);
                                Debug.Log("Enabled debug mode for SegmentationManager");
                            }
                            
                            EditorUtility.SetDirty(segManagerObj);
                        }
                        else
                        {
                            Debug.LogWarning("Could not find ModelAsset field in SegmentationManager");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Could not find model.onnx at Assets/ML/Models/model.onnx");
                    }
                    
                    // Добавляем ModelFixer для автоматического исправления настроек SegmentationManager
                    try 
                    {
                        // Проверяем, существует ли тип ModelFixer
                        Type modelFixerType = Type.GetType("ModelFixer, Assembly-CSharp");
                        if (modelFixerType != null)
                        {
                            // Добавляем ModelFixer к объекту SegmentationManager
                            var modelFixer = segManagerObj.AddComponent(modelFixerType);
                            
                            // Устанавливаем ссылку на SegmentationManager
                            var segManagerField = modelFixerType.GetField("segmentationManager");
                            if (segManagerField != null && segManager != null)
                            {
                                segManagerField.SetValue(modelFixer, segManager);
                                Debug.Log("Added ModelFixer to SegmentationManager for automatic configuration");
                            }
                        }
                        else
                        {
                            // Если не найден через Type.GetType, пробуем прямое добавление
                            try 
                            {
                                var modelFixer = segManagerObj.AddComponent<ModelFixer>();
                                if (modelFixer != null && segManager != null)
                                {
                                    modelFixer.segmentationManager = (SegmentationManager)segManager;
                                    Debug.Log("Added ModelFixer to SegmentationManager for automatic configuration");
                                }
                            }
                            catch (Exception)
                            {
                                Debug.LogWarning("Could not add ModelFixer - type not found. The SegmentationManager may require manual configuration.");
                            }
                        }
                    }
                    catch (Exception fixerEx)
                    {
                        Debug.LogWarning($"Error adding ModelFixer: {fixerEx.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning("Could not create SegmentationManager - type not found. Make sure ML System is properly imported.");
                    UnityEngine.Object.DestroyImmediate(segManagerObj);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating SegmentationManager: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Создает и настраивает MLManagerAdapter для работы с SegmentationManager
        /// </summary>
        private static void CreateMLManagerAdapter()
        {
            try
            {
                // Проверяем, существует ли MLManagerAdapter
                Type mlManagerAdapterType = Type.GetType("MLManagerAdapter, Assembly-CSharp");
                Component existingAdapter = null;
                
                if (mlManagerAdapterType != null)
                {
                    existingAdapter = (Component)FindObjectOfType(mlManagerAdapterType);
                }
                else
                {
                    try
                    {
                        existingAdapter = FindObjectOfType<MLManagerAdapter>();
                    }
                    catch (Exception)
                    {
                        Debug.LogWarning("MLManagerAdapter type not found directly");
                    }
                }
                
                if (existingAdapter != null)
                {
                    Debug.Log("Using existing MLManagerAdapter");
                    
                    // Настройка параметров существующего MLManagerAdapter
                    try
                    {
                        // Установка правильного интервала обработки
                        var processingIntervalField = existingAdapter.GetType().GetField("processingInterval", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (processingIntervalField != null)
                        {
                            float currentInterval = (float)processingIntervalField.GetValue(existingAdapter);
                            if (currentInterval > 0.5f)
                            {
                                processingIntervalField.SetValue(existingAdapter, 0.5f);
                                Debug.Log("Set processing interval to 0.5 for existing MLManagerAdapter");
                                EditorUtility.SetDirty(existingAdapter);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error configuring existing MLManagerAdapter: {ex.Message}");
                    }
                    
                    return;
                }
                
                // Создаем новый MLManagerAdapter
                GameObject mlManagerObj = new GameObject("MLManagerAdapter");
                
                // Добавляем компонент MLManagerAdapter
                Component mlManagerAdapter = null;
                
                if (mlManagerAdapterType != null)
                {
                    mlManagerAdapter = mlManagerObj.AddComponent(mlManagerAdapterType);
                    Debug.Log("Created MLManagerAdapter");
                    
                    // Настройка параметров нового MLManagerAdapter
                    try
                    {
                        // Установка правильного интервала обработки
                        var processingIntervalField = mlManagerAdapterType.GetField("processingInterval", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (processingIntervalField != null)
                        {
                            processingIntervalField.SetValue(mlManagerAdapter, 0.5f);
                            Debug.Log("Set processing interval to 0.5 for new MLManagerAdapter");
                        }
                        
                        // Находим и связываем с SegmentationManager
                        var segmentationManager = FindFirstObjectByType<SegmentationManager>();
                        if (segmentationManager != null)
                        {
                            var segManagerField = mlManagerAdapterType.GetField("segmentationManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (segManagerField != null)
                            {
                                segManagerField.SetValue(mlManagerAdapter, segmentationManager);
                                Debug.Log("Linked MLManagerAdapter with SegmentationManager");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("SegmentationManager not found to link with MLManagerAdapter");
                        }
                        
                        EditorUtility.SetDirty(mlManagerObj);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error configuring new MLManagerAdapter: {ex.Message}");
                    }
                }
                else
                {
                    try
                    {
                        mlManagerAdapter = mlManagerObj.AddComponent<MLManagerAdapter>();
                        Debug.Log("Created MLManagerAdapter directly");
                        
                        // Настройка параметров напрямую
                        if (mlManagerAdapter != null)
                        {
                            var propertyInfo = mlManagerAdapter.GetType().GetProperty("ProcessingInterval");
                            if (propertyInfo != null && propertyInfo.CanWrite)
                            {
                                propertyInfo.SetValue(mlManagerAdapter, 0.5f);
                                Debug.Log("Set processing interval to 0.5 for new MLManagerAdapter");
                            }
                            
                            // Находим и связываем с SegmentationManager
                            var segmentationManager = FindFirstObjectByType<SegmentationManager>();
                            if (segmentationManager != null)
                            {
                                var propertyInfoSeg = mlManagerAdapter.GetType().GetProperty("SegmentationManager");
                                if (propertyInfoSeg != null && propertyInfoSeg.CanWrite)
                                {
                                    propertyInfoSeg.SetValue(mlManagerAdapter, segmentationManager);
                                    Debug.Log("Linked MLManagerAdapter with SegmentationManager");
                                }
                            }
                            
                            EditorUtility.SetDirty(mlManagerObj);
                        }
                    }
                    catch (Exception)
                    {
                        Debug.LogWarning("Could not create MLManagerAdapter - type not found. ML segmentation may not work properly.");
                        UnityEngine.Object.DestroyImmediate(mlManagerObj);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating MLManagerAdapter: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Создает и настраивает визуализатор AR плоскостей
        /// </summary>
        private static void CreateARPlaneVisualizer(XROrigin xrOrigin)
        {
            try
            {
                // Проверяем, существует ли уже визуализатор
                GameObject existingVisualizer = GameObject.Find("AR Plane Visualizer");
                if (existingVisualizer != null)
                {
                    Debug.Log("Using existing AR Plane Visualizer");
                    return;
                }
                
                // Создаем новый визуализатор
                GameObject visualizerObj = new GameObject("AR Plane Visualizer");
                
                // Получаем ARPlaneManager с родительского XR Origin
                ARPlaneManager planeManager = null;
                if (xrOrigin != null && xrOrigin.gameObject != null)
                {
                    planeManager = xrOrigin.gameObject.GetComponent<ARPlaneManager>();
                }
                
                // Если ARPlaneManager не найден, добавляем предупреждение
                if (planeManager == null)
                {
                    Debug.LogWarning("ARPlaneManager not found on XR Origin. AR Plane Visualizer may not work correctly.");
                }
                else
                {
                    // Добавляем скрипт для визуализации плоскостей (если он существует)
                    Type visualizerType = Type.GetType("ARPlaneVisualizer, Assembly-CSharp");
                    if (visualizerType != null)
                    {
                        var visualizer = visualizerObj.AddComponent(visualizerType);
                        Debug.Log("Added ARPlaneVisualizer component");
                        
                        // Если у компонента есть поле для ссылки на planeManager, устанавливаем его
                        var planeManagerField = visualizerType.GetField("planeManager");
                        if (planeManagerField != null)
                        {
                            planeManagerField.SetValue(visualizer, planeManager);
                        }
                    }
                    else
                    {
                        // Добавляем компонент-заглушку для отслеживания плоскостей
                        visualizerObj.AddComponent<ARPlaneMeshVisualizer>();
                        Debug.Log("Added ARPlaneMeshVisualizer component");
                        
                        // Добавляем ссылку на ARPlanesParent для отслеживания созданных плоскостей
                        GameObject planesParent = new GameObject("Trackables");
                        planesParent.transform.SetParent(visualizerObj.transform, false);
                        
                        // Настраиваем ARPlaneManager, чтобы использовать этот объект для визуализации
                        if (planeManager != null)
                        {
                            // Проверяем наличие префаба
                            bool prefabFound = false;
                            
                            // Проверяем существование папки Resources/Prefabs
                            if (AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
                            {
                                // Загружаем префаб для плоскостей (горизонтальных и вертикальных)
                                GameObject defaultPlanePrefab = Resources.Load<GameObject>("Prefabs/DefaultARPlane");
                                prefabFound = defaultPlanePrefab != null;
                                
                                if (prefabFound)
                                {
                                    planeManager.planePrefab = defaultPlanePrefab;
                                    Debug.Log("Found and assigned DefaultARPlane prefab from Resources/Prefabs");
                                }
                            }
                            else
                            {
                                Debug.LogWarning("Resources/Prefabs folder not found. Will create default plane prefab.");
                            }
                            
                            // Если префаб не найден, создаем простой префаб
                            if (!prefabFound)
                            {
                                // Создаем простой префаб для плоскостей
                                GameObject newPlanePrefab = new GameObject("DefaultARPlane");
                                newPlanePrefab.AddComponent<ARPlaneMeshVisualizer>();
                                var meshFilter = newPlanePrefab.AddComponent<MeshFilter>();
                                var meshRenderer = newPlanePrefab.AddComponent<MeshRenderer>();
                                
                                // Проверяем, существует ли шейдер
                                Shader planeShader = Shader.Find("AR/PlaneWithTexture");
                                if (planeShader != null)
                                {
                                    meshRenderer.sharedMaterial = new Material(planeShader);
                                    Debug.Log("Found AR/PlaneWithTexture shader and assigned to plane material");
                                }
                                else
                                {
                                    // Используем стандартный шейдер, если специального нет
                                    meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
                                    Debug.LogWarning("AR/PlaneWithTexture shader not found. Using Standard shader instead.");
                                }
                                
                                newPlanePrefab.AddComponent<BoxCollider>();
                                
                                // Сохраняем новый префаб, если есть папка Resources
                                if (AssetDatabase.IsValidFolder("Assets/Resources"))
                                {
                                    // Проверяем наличие папки Prefabs в Resources
                                    if (!AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
                                    {
                                        AssetDatabase.CreateFolder("Assets/Resources", "Prefabs");
                                    }
                                    
                                    // Сохраняем префаб
                                    string prefabPath = "Assets/Resources/Prefabs/DefaultARPlane.prefab";
                                    
                                    // Сначала делаем префаб временным объектом на сцене
                                    GameObject tempPrefab = UnityEngine.Object.Instantiate(newPlanePrefab);
                                    
                                    // Создаем или перезаписываем файл префаба
                                    bool success = false;
                                    try 
                                    {
                                        #if UNITY_2018_3_OR_NEWER
                                        GameObject createdPrefab = PrefabUtility.SaveAsPrefabAsset(tempPrefab, prefabPath);
                                        success = createdPrefab != null;
                                        #else
                                        success = PrefabUtility.CreatePrefab(prefabPath, tempPrefab);
                                        #endif
                                    }
                                    catch (System.Exception e)
                                    {
                                        Debug.LogError($"Failed to save plane prefab: {e.Message}");
                                    }
                                    
                                    // Удаляем временный объект
                                    UnityEngine.Object.DestroyImmediate(tempPrefab);
                                    
                                    if (success)
                                    {
                                        // Загружаем созданный префаб из ресурсов
                                        GameObject savedPrefab = Resources.Load<GameObject>("Prefabs/DefaultARPlane");
                                        if (savedPrefab != null)
                                        {
                                            planeManager.planePrefab = savedPrefab;
                                            Debug.Log("Created and saved DefaultARPlane prefab to Resources/Prefabs");
                                        }
                                    }
                                    else 
                                    {
                                        planeManager.planePrefab = newPlanePrefab;
                                        Debug.LogWarning("Failed to save prefab. Using temporary prefab instead.");
                                    }
                                }
                                else
                                {
                                    planeManager.planePrefab = newPlanePrefab;
                                    Debug.LogWarning("Resources folder not found. Using temporary prefab.");
                                }
                            }
                            
                            Debug.Log("Configured AR Plane Manager with plane prefab");
                            
                            // Добавим обработчик события добавления плоскости
                            EditorApplication.delayCall += () => {
                                try {
                                    // Находим ARPlaneManager снова
                                    var pm = FindObjectOfType<ARPlaneManager>();
                                    if (pm != null)
                                    {
                                        // Добавляем компонент, обрабатывающий события добавления плоскостей
                                        GameObject planeEventsHandler = new GameObject("PlaneEventsHandler");
                                        planeEventsHandler.transform.SetParent(visualizerObj.transform, false);
                                        
                                        // Проверяем доступность типа ARPlaneEventsHandler
                                        try
                                        {
                                            var handlerType = System.Type.GetType("ARPlaneEventsHandler, Assembly-CSharp");
                                            if (handlerType != null)
                                            {
                                                planeEventsHandler.AddComponent(handlerType);
                                                Debug.Log("Added ARPlaneEventsHandler component from Assembly-CSharp");
                                            }
                                            else
                                            {
                                                // Пробуем добавить напрямую по имени класса
                                                try 
                                                {
                                                    planeEventsHandler.AddComponent<ARPlaneEventsHandler>();
                                                    Debug.Log("Added ARPlaneEventsHandler component directly");
                                                }
                                                catch (System.Exception)
                                                {
                                                    Debug.LogWarning("ARPlaneEventsHandler type not available, plane events handling might not work properly");
                                                }
                                            }
                                        }
                                        catch (System.Exception e)
                                        {
                                            Debug.LogError($"Error adding plane events handler: {e.Message}");
                                        }
                                    }
                                }
                                catch (System.Exception e)
                                {
                                    Debug.LogError($"Failed to setup plane events handler: {e.Message}");
                                }
                            };
                        }
                    }
                }
                
                // Добавляем скрипт для фиксации иерархии визуализатора плоскостей
                GameObject fixerObj = new GameObject("AR Plane Visualizer Fixer");
                Type fixerType = Type.GetType("ARPlaneVisualizerFixer, Assembly-CSharp");
                if (fixerType != null)
                {
                    fixerObj.AddComponent(fixerType);
                    Debug.Log("Added AR Plane Visualizer Fixer");
                }
                
                Debug.Log("AR Plane Visualizer setup completed");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating AR Plane Visualizer: {ex.Message}");
            }
        }

        /// <summary>
        /// Назначает префаб плоскостей для ARPlaneManager
        /// </summary>
        private static void SetupPlanePrefab(ARPlaneManager planeManager)
        {
            // Проверяем наличие префаба
            bool prefabFound = false;
            
            // Проверяем существование папки Resources/Prefabs
            if (AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
            {
                // Загружаем префаб для плоскостей (горизонтальных и вертикальных)
                GameObject defaultPlanePrefab = Resources.Load<GameObject>("Prefabs/DefaultARPlane");
                prefabFound = defaultPlanePrefab != null;
                
                if (prefabFound)
                {
                    planeManager.planePrefab = defaultPlanePrefab;
                    Debug.Log("Found and assigned DefaultARPlane prefab from Resources/Prefabs");
                }
            }
            else
            {
                Debug.LogWarning("Resources/Prefabs folder not found. Will create default plane prefab.");
            }
            
            // Если префаб не найден, создаем простой префаб
            if (!prefabFound)
            {
                // Создаем простой префаб для плоскостей
                GameObject newPlanePrefab = new GameObject("DefaultARPlane");
                newPlanePrefab.AddComponent<ARPlaneMeshVisualizer>();
                var meshFilter = newPlanePrefab.AddComponent<MeshFilter>();
                var meshRenderer = newPlanePrefab.AddComponent<MeshRenderer>();
                
                // Проверяем, существует ли шейдер
                Shader planeShader = Shader.Find("AR/PlaneWithTexture");
                if (planeShader != null)
                {
                    meshRenderer.sharedMaterial = new Material(planeShader);
                    Debug.Log("Found AR/PlaneWithTexture shader and assigned to plane material");
                }
                else
                {
                    // Используем стандартный шейдер, если специального нет
                    meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
                    Debug.LogWarning("AR/PlaneWithTexture shader not found. Using Standard shader instead.");
                }
                
                newPlanePrefab.AddComponent<BoxCollider>();
                
                // Сохраняем новый префаб, если есть папка Resources
                if (AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    // Проверяем наличие папки Prefabs в Resources
                    if (!AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
                    {
                        AssetDatabase.CreateFolder("Assets/Resources", "Prefabs");
                    }
                    
                    // Сохраняем префаб
                    string prefabPath = "Assets/Resources/Prefabs/DefaultARPlane.prefab";
                    
                    // Сначала делаем префаб временным объектом на сцене
                    GameObject tempPrefab = UnityEngine.Object.Instantiate(newPlanePrefab);
                    
                    // Создаем или перезаписываем файл префаба
                    bool success = false;
                    try 
                    {
                        #if UNITY_2018_3_OR_NEWER
                        GameObject createdPrefab = PrefabUtility.SaveAsPrefabAsset(tempPrefab, prefabPath);
                        success = createdPrefab != null;
                        #else
                        success = PrefabUtility.CreatePrefab(prefabPath, tempPrefab);
                        #endif
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to save plane prefab: {e.Message}");
                    }
                    
                    // Удаляем временный объект
                    UnityEngine.Object.DestroyImmediate(tempPrefab);
                    
                    if (success)
                    {
                        // Загружаем созданный префаб из ресурсов
                        GameObject savedPrefab = Resources.Load<GameObject>("Prefabs/DefaultARPlane");
                        if (savedPrefab != null)
                        {
                            planeManager.planePrefab = savedPrefab;
                            Debug.Log("Created and saved DefaultARPlane prefab to Resources/Prefabs");
                        }
                    }
                    else 
                    {
                        planeManager.planePrefab = newPlanePrefab;
                        Debug.LogWarning("Failed to save prefab. Using temporary prefab instead.");
                    }
                }
                else
                {
                    planeManager.planePrefab = newPlanePrefab;
                    Debug.LogWarning("Resources folder not found. Using temporary prefab.");
                }
            }
            
            Debug.Log("Configured AR Plane Manager with plane prefab");
        }

        /// <summary>
        /// Creates the Wall Painter Controller for AR painting functionality
        /// </summary>
        private static void CreateWallPainterController(XROrigin xrOrigin)
        {
            try
            {
                // Check if ARWallPainterController already exists
                var existingController = FindFirstObjectByType<ARWallPainterController>();
                if (existingController != null)
                {
                    Debug.Log("Using existing ARWallPainterController");
                    return;
                }
                
                // Create a new ARWallPainterController
                GameObject painterObj = new GameObject("AR Wall Painter Controller");
                
                // Try to add the component directly if it exists
                Type controllerType = Type.GetType("ARWallPainterController, Assembly-CSharp");
                if (controllerType != null)
                {
                    painterObj.AddComponent(controllerType);
                    Debug.Log("Created Wall Painter Controller");
                }
                else
                {
                    // If type lookup fails, try adding the component directly
                    // This approach requires the type to be already known at compile time
                    try
                    {
                        var controller = painterObj.AddComponent<ARWallPainterController>();
                        
                        // Set references if possible
                        if (xrOrigin != null && controller != null)
                        {
                            // Set ARCamera reference - implementation depends on actual controller properties
                            var cameraField = controllerType?.GetField("arCamera");
                            if (cameraField != null && xrOrigin.Camera != null)
                            {
                                cameraField.SetValue(controller, xrOrigin.Camera);
                            }
                        }
                        
                        Debug.Log("Created Wall Painter Controller");
                    }
                    catch (Exception)
                    {
                        Debug.LogWarning("Could not create ARWallPainterController - type not found. Make sure AR Wall Painter is properly imported.");
                        UnityEngine.Object.DestroyImmediate(painterObj);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error creating Wall Painter Controller: {ex.Message}");
            }
        }

        [MenuItem("AR/ML Tools/Fix SegmentationManager Model Configuration")]
        public static void FixSegmentationManagerModelConfiguration()
        {
            // Call the ModelFixerMenu's method directly
            ModelFixerMenu.FixModelShapeIssues();
        }
}

/// <summary>
/// Компонент, который автоматически создает ARMeshManagerChecker при запуске
/// </summary>
public class BasicARMeshManagerCheckerStarter : MonoBehaviour
{
    public XROrigin targetXROrigin;
    
    void Start()
    {
        // Сам компонент ничего не делает в редакторе,
        // но будет использован для создания ARMeshManagerChecker при запуске игры
        }
    }
} // namespace ARSceneSetupUtils 