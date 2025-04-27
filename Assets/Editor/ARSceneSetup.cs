using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System.IO;
using UnityEditor.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using System;
using UnityEngine.SceneManagement;
using ML.DeepLab;

public class ARSceneSetup : EditorWindow
{
    private const string MODEL_PATH = "Assets/ML/Models/model.onnx";
    private const string WALL_SHADER_PATH = "Assets/Shaders/WallMaskShader.shader";
    private const int WALL_CLASS_ID = 7;
    private const int SEGFORMER_INPUT_SIZE = 512;
    
    // Helper method to avoid SendMessage warnings by deferring component addition
    private static T SafeAddComponent<T>(GameObject target) where T : Component
    {
        // Check if component already exists
        T existing = target.GetComponent<T>();
        if (existing != null)
            return existing;
            
        // Queue the component addition for next editor update to avoid SendMessage warnings
        T component = null;
        EditorApplication.delayCall += () => 
        {
            if (target != null) // Check if target still exists
            {
                component = target.AddComponent<T>();
                EditorUtility.SetDirty(target);
            }
        };
        
        // Need to create the component now for immediate reference
        // The duplicated component will be removed on next editor update
        return target.AddComponent<T>();
    }
    
    [MenuItem("AR/Setup AR Scene")]
    public static void SetupARScene()
    {
        // Создаем новую сцену
        Scene newScene = CreateARScene();
        
        // Добавляем необходимые AR компоненты
        GameObject arSessionOrigin = CreateARComponents();
        
        // Добавляем интерфейс управления (только палитру цветов)
        GameObject uiCanvas = CreateARUICanvas();
        
        // Сохраняем сцену
        string scenePath = EditorUtility.SaveFilePanel("Save AR Scene", "Assets", "ARScene", "unity");
        if (!string.IsNullOrEmpty(scenePath))
        {
            // Преобразуем абсолютный путь в относительный путь проекта
            string relativePath = scenePath.Substring(Application.dataPath.Length - "Assets".Length);
            EditorSceneManager.SaveScene(newScene, relativePath);
            Debug.Log("AR сцена создана и сохранена в: " + relativePath);
        }
        else
        {
            Debug.Log("AR сцена создана, но не сохранена.");
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
        Light light = directionalLight.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(1.0f, 0.95f, 0.84f);
        directionalLight.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        return newScene;
    }
    
    private static GameObject SetupARSystem()
    {
        try
        {
            // Create root object for AR system
            GameObject arSystem = new GameObject("AR System");
            
            // Add AR Session
            GameObject arSessionObj = new GameObject("AR Session");
            ARSession arSession = arSessionObj.AddComponent<ARSession>();
            arSession.enabled = false; // Disable auto-start
            arSessionObj.transform.SetParent(arSystem.transform);
            
            // Add ARSessionHelper for initialization management
            arSessionObj.AddComponent<ARSessionHelper>();
            
            // Create XR Origin with child objects
            GameObject xrOriginObj = new GameObject("XR Origin");
            xrOriginObj.transform.SetParent(arSystem.transform);
            
            // Create Camera Offset - MUST be created BEFORE adding XROrigin component
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginObj.transform);
            
            // Create AR Camera as a child of Camera Offset
            GameObject arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(cameraOffset.transform);
            
            // Add camera components
            Camera camera = arCamera.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.depth = 0;
            arCamera.tag = "MainCamera";
            
            // Find and disable/destroy any existing Main Camera to avoid conflicts
            GameObject existingMainCamera = GameObject.FindWithTag("MainCamera");
            if (existingMainCamera != null && existingMainCamera != arCamera)
            {
                Debug.Log("Disabling existing Main Camera to avoid conflicts with AR Camera");
                existingMainCamera.tag = "Untagged"; // Remove the MainCamera tag
                existingMainCamera.SetActive(false); // Disable it
            }
            
            // AR components for camera
            ARCameraManager arCameraManager = arCamera.AddComponent<ARCameraManager>();
            ARCameraBackground arCameraBackground = arCamera.AddComponent<ARCameraBackground>();
            
            // Configure AR camera settings for proper visualization
            if (camera != null)
            {
                camera.cullingMask = -1; // Include all layers
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30f;
                Debug.Log("AR Camera settings configured for maximum visibility");
            }
            
            // Add Tracked Pose Driver
            var trackedPoseDriver = arCamera.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver != null)
            {
                trackedPoseDriver.positionAction = new InputAction(
                    name: "Position",
                    type: InputActionType.Value,
                    binding: "<XRHMD>/centerEyePosition",
                    expectedControlType: "Vector3"
                );
                trackedPoseDriver.rotationAction = new InputAction(
                    name: "Rotation",
                    type: InputActionType.Value,
                    binding: "<XRHMD>/centerEyeRotation",
                    expectedControlType: "Quaternion"
                );
                
                // Enable Input System actions
                trackedPoseDriver.positionAction.Enable();
                trackedPoseDriver.rotationAction.Enable();
            }
            
            // Configure XR Origin
            XROrigin xrOrigin = xrOriginObj.AddComponent<XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.Camera = camera;
                xrOrigin.CameraFloorOffsetObject = cameraOffset;
                xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            }
            
            // Добавляем компоненты для обнаружения поверхностей
            ARPlaneManager planeManager = xrOriginObj.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.planePrefab = CreatePlanePrefab();

            // Добавляем WallPlaneFilter для фильтрации плоскостей
            WallPlaneFilter wallPlaneFilter = xrOriginObj.AddComponent<WallPlaneFilter>();
            wallPlaneFilter.minPlaneArea = 0.5f;
            wallPlaneFilter.minVerticalCos = 0.8f;
            Debug.Log("Added WallPlaneFilter component for filtering vertical planes");

            // Create AR Mesh Manager as direct child of XR Origin (not under CameraOffset)
            // This is critical for proper world-space positioning of meshes
            GameObject meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOriginObj.transform);
            
            // Add ARMeshManager directly to XROrigin
            ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
            meshManager.density = 0.5f;
            Debug.Log("Added ARMeshManager as child of XROrigin for mesh processing in world space");
            
            // Add WallAligner to handle applying material to walls
            WallAligner wallAligner = SafeAddComponent<WallAligner>(meshManagerObj);
            
            // Load wall material
            Material simpleWallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SimpleWallMaterial.mat");
            if (simpleWallMaterial == null)
            {
                simpleWallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMaterial.mat");
            }
            
            if (simpleWallMaterial != null)
            {
                // Set material for WallAligner
                SerializedObject serializedWallAligner = new SerializedObject(wallAligner);
                var wallMaterialProp = serializedWallAligner.FindProperty("wallMaterial");
                if (wallMaterialProp != null)
                {
                    wallMaterialProp.objectReferenceValue = simpleWallMaterial;
                }
                serializedWallAligner.ApplyModifiedProperties();
                Debug.Log("Added WallAligner with material assigned for automatic wall mesh processing");
            }
            else
            {
                Debug.LogWarning("Wall material not found. Please assign it manually to WallAligner component.");
            }

            // Disable WallMeshRenderer if it exists
            var wallMeshRenderer = meshManagerObj.GetComponent<WallMeshRenderer>();
            if (wallMeshRenderer != null)
            {
                wallMeshRenderer.enabled = false;
                Debug.Log("Disabled WallMeshRenderer to avoid conflicts with WallAligner");
            }

            // Add Raycast Manager for surface detection
            ARRaycastManager raycastManager = xrOriginObj.AddComponent<ARRaycastManager>();
            
            // Add Anchor Manager for creating and managing anchors
            ARAnchorManager anchorManager = xrOriginObj.AddComponent<ARAnchorManager>();
            Debug.Log("Added ARAnchorManager for creating and managing AR anchors");

            // Create and configure ARManager
            GameObject arManagerObj = new GameObject("AR Manager");
            arManagerObj.transform.SetParent(arSystem.transform);
            ARManager arManager = arManagerObj.AddComponent<ARManager>();
            
            // Set references to necessary components
            SerializedObject serializedARManager = new SerializedObject(arManager);
            var arSessionProp = serializedARManager.FindProperty("arSession");
            if (arSessionProp != null)
            {
                arSessionProp.objectReferenceValue = arSession;
            }
            
            var planeManagerProp = serializedARManager.FindProperty("planeManager");
            if (planeManagerProp != null)
            {
                planeManagerProp.objectReferenceValue = planeManager;
            }
            
            serializedARManager.ApplyModifiedProperties();
            Debug.Log("ARManager created and configured successfully");

            // Add SurfaceDetector
            SurfaceDetector surfaceDetector = xrOriginObj.AddComponent<SurfaceDetector>();

            // Add PlaneVisualizer for displaying detected surfaces
            PlaneVisualizer planeVisualizer = xrOriginObj.AddComponent<PlaneVisualizer>();

            // Create material for plane visualization
            Material planeMaterial = new Material(Shader.Find("Standard"));
            planeMaterial.color = new Color(0.0f, 0.8f, 1.0f, 0.5f);
            planeMaterial.SetFloat("_Mode", 3); // Transparent mode
            planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            planeMaterial.SetInt("_ZWrite", 0);
            planeMaterial.DisableKeyword("_ALPHATEST_ON");
            planeMaterial.EnableKeyword("_ALPHABLEND_ON");
            planeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            planeMaterial.renderQueue = 3000;

            // Save material to project
            if (!Directory.Exists("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
            AssetDatabase.CreateAsset(planeMaterial, "Assets/Materials/PlaneMaterial.mat");
            AssetDatabase.SaveAssets();

            Debug.Log("Surface detection components have been added successfully");

            return arSystem;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in AR System setup: {e.Message}\n{e.StackTrace}");
            throw;
        }
    }
    
    private static GameObject CreatePlanePrefab()
    {
        GameObject planePrefab = new GameObject("AR Plane Visual");
        planePrefab.AddComponent<ARPlane>();
        planePrefab.AddComponent<MeshFilter>();
        planePrefab.AddComponent<MeshRenderer>();
        planePrefab.AddComponent<ARPlaneMeshVisualizer>();
        planePrefab.AddComponent<LineRenderer>();

        // Сохраняем префаб
        if (!Directory.Exists("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        string prefabPath = "Assets/Prefabs/ARPlaneVisual.prefab";
        PrefabUtility.SaveAsPrefabAsset(planePrefab, prefabPath);
        GameObject.DestroyImmediate(planePrefab);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }
    
    private static void SetupMLComponents(GameObject parent)
    {
        try
        {
            if (parent == null)
            {
                Debug.LogError("Parent object is null. Cannot setup ML components.");
                return;
            }
            
            // Check if DeepLabV3 model exists
            if (!File.Exists(MODEL_PATH))
            {
                Debug.LogError("ONNX model not found at path: " + MODEL_PATH);
                // Continue with setup but warn the user
            }
            
            // Create ML objects and components
            GameObject mlSystem = new GameObject("ML System");
            mlSystem.transform.SetParent(parent.transform);
            
            // Load model - handle the case when model doesn't exist
            Unity.Barracuda.NNModel model = null;
            if (File.Exists(MODEL_PATH))
            {
                model = AssetDatabase.LoadAssetAtPath<Unity.Barracuda.NNModel>(MODEL_PATH);
                if (model == null)
                {
                    Debug.LogError("Failed to load ONNX model as Barracuda NNModel. Will continue without model reference.");
                }
            }
            
            // Create DeepLab Predictor
            GameObject deepLabObj = new GameObject("DeepLab Predictor");
            deepLabObj.transform.SetParent(mlSystem.transform);
            DeepLabPredictor predictor = deepLabObj.AddComponent<DeepLabPredictor>();
            
            // Only set model reference if it exists
            if (model != null)
            {
                predictor.modelAsset = model;
            }
            
            predictor.inputWidth = 513;
            predictor.inputHeight = 513;
            predictor.WallClassId = 9;
            
            // Try to set these properties safely
            var predictorSerialized = new SerializedObject(predictor);
            var enableDebugProperty = predictorSerialized.FindProperty("enableDebugLogging");
            if (enableDebugProperty != null)
            {
                enableDebugProperty.boolValue = true;
            }
            
            var thresholdProperty = predictorSerialized.FindProperty("ClassificationThreshold");
            if (thresholdProperty != null)
            {
                thresholdProperty.floatValue = 0.05f;
            }
            predictorSerialized.ApplyModifiedProperties();
            
            // Create Enhanced DeepLab Predictor for better wall detection
            GameObject enhancedDeepLabObj = new GameObject("Enhanced DeepLab Predictor");
            enhancedDeepLabObj.transform.SetParent(mlSystem.transform);
            EnhancedDeepLabPredictor enhancedPredictor = enhancedDeepLabObj.AddComponent<EnhancedDeepLabPredictor>();
            
            // Copy settings from basic predictor
            enhancedPredictor.modelAsset = predictor.modelAsset;
            enhancedPredictor.WallClassId = (byte)9;
            enhancedPredictor.ClassificationThreshold = 0.5f;
            enhancedPredictor.useArgMaxMode = true;
            enhancedPredictor.debugMode = true;
            
            Debug.Log("Created EnhancedDeepLabPredictor for improved wall detection");
            
            // Create ML Manager
            GameObject mlManagerObj = new GameObject("ML Manager");
            mlManagerObj.transform.SetParent(mlSystem.transform);
            MLManager mlManager = mlManagerObj.AddComponent<MLManager>();
            
            // Set reference to DeepLabPredictor in MLManager
            SerializedObject serializedMlManager = new SerializedObject(mlManager);
            var deepLabProperty = serializedMlManager.FindProperty("deepLabPredictor");
            if (deepLabProperty != null)
            {
                deepLabProperty.objectReferenceValue = predictor;
            }
            
            var intervalProperty = serializedMlManager.FindProperty("predictionInterval");
            if (intervalProperty != null) 
            {
                intervalProperty.floatValue = 0.5f;
            }
            serializedMlManager.ApplyModifiedProperties();
            
            // Add enhanced wall detection system
            GameObject wallDetectionObj = new GameObject("Wall Detection System");
            wallDetectionObj.transform.SetParent(mlSystem.transform);
            
            // Setup the WallDetectionSetup component
            WallDetectionSetup wallDetectionSetup = wallDetectionObj.AddComponent<WallDetectionSetup>();
            wallDetectionSetup.autoSetup = true;
            wallDetectionSetup.useEnhancedPredictor = true;
            wallDetectionSetup.createUIPanel = false; // Отключаем создание UI панели
            wallDetectionSetup.applyFixesToOriginal = true;
            wallDetectionSetup.initialThreshold = 0.3f;
            wallDetectionSetup.wallClassId = 9;
            wallDetectionSetup.useArgMaxMode = true;
            
            Debug.Log("Wall Detection System added to scene with enhanced recognition");

            // Add our new AR-aware wall anchoring system
            GameObject remaluxWallSystemObj = new GameObject("Remalux AR Wall System");
            remaluxWallSystemObj.transform.SetParent(mlSystem.transform);
            
            // Add RemaluxARWallSetup component for automatic configuration
            RemaluxARWallSetup arWallSetup = remaluxWallSystemObj.AddComponent<RemaluxARWallSetup>();
            // Set public properties using SerializedObject to avoid direct access to private fields
            SerializedObject serializedArWallSetup = new SerializedObject(arWallSetup);
            serializedArWallSetup.FindProperty("_autoSetup").boolValue = true;
            serializedArWallSetup.FindProperty("_predictor").objectReferenceValue = enhancedPredictor;
            
            // Find needed AR components
            ARSession arSession = GameObject.FindObjectOfType<ARSession>();
            ARCameraManager arCameraManager = GameObject.FindObjectOfType<ARCameraManager>();
            ARPlaneManager arPlaneManager = GameObject.FindObjectOfType<ARPlaneManager>();
            ARRaycastManager arRaycastManager = GameObject.FindObjectOfType<ARRaycastManager>();
            
            serializedArWallSetup.FindProperty("_arSession").objectReferenceValue = arSession;
            serializedArWallSetup.FindProperty("_arCameraManager").objectReferenceValue = arCameraManager;
            serializedArWallSetup.FindProperty("_arPlaneManager").objectReferenceValue = arPlaneManager;
            serializedArWallSetup.FindProperty("_arRaycastManager").objectReferenceValue = arRaycastManager;
            
            // Set wall color
            serializedArWallSetup.FindProperty("_wallColor").colorValue = new Color(0.2f, 0.8f, 1.0f, 0.7f);
            
            // Try to load wall material if it exists
            Material existingWallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMaterial.mat");
            if (existingWallMaterial != null)
            {
                serializedArWallSetup.FindProperty("_wallMaterial").objectReferenceValue = existingWallMaterial;
            }
            
            // Apply all changes
            serializedArWallSetup.ApplyModifiedProperties();
            
            Debug.Log("Added Remalux AR Wall anchoring system for fixing walls in AR space");

            // We won't add WallMeshRenderer here - it needs to be on an XROrigin
            // We'll create a reference for it in SetupComponentReferences instead
            
            // We'll set up the display image after the UI is created
            // so we'll just create a reference to find it later
            
            // Check for wall shader
            Shader wallShader = null;
            if (File.Exists(WALL_SHADER_PATH))
            {
                wallShader = AssetDatabase.LoadAssetAtPath<Shader>(WALL_SHADER_PATH);
            }
            
            if (wallShader == null)
            {
                Debug.LogWarning("WallMaskShader not found at " + WALL_SHADER_PATH + ". Using Standard shader instead.");
                wallShader = Shader.Find("Standard");
            }
            
            // Create material for walls
            if (!Directory.Exists("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
                
            // Rename variable to avoid name conflict
            Material newWallMaterial;
            if (wallShader != null)
            {
                newWallMaterial = new Material(wallShader);
                newWallMaterial.SetColor("_Color", new Color(0.5f, 0.8f, 1f, 0.7f));
                
                // Use try-catch to avoid errors with missing properties
                try { newWallMaterial.SetFloat("_Opacity", 0.7f); } catch { }
                try { newWallMaterial.SetFloat("_Threshold", 0.03f); } catch { }
                try { newWallMaterial.SetFloat("_SmoothFactor", 0.01f); } catch { }
                try { newWallMaterial.SetFloat("_EdgeEnhance", 1.2f); } catch { }
            }
            else
            {
                // Fallback to standard transparent material
                wallShader = Shader.Find("Standard");
                if (wallShader == null)
                {
                    // Ultimate fallback - create a default material
                    newWallMaterial = new Material(Shader.Find("Diffuse"));
                    newWallMaterial.color = new Color(0.5f, 0.8f, 1f, 0.7f);
                }
                else
                {
                    newWallMaterial = new Material(wallShader);
                    newWallMaterial.color = new Color(0.5f, 0.8f, 1f, 0.7f);
                    
                    try
                    {
                        newWallMaterial.SetFloat("_Mode", 3); // Transparent mode
                        newWallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        newWallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        newWallMaterial.SetInt("_ZWrite", 0);
                        newWallMaterial.DisableKeyword("_ALPHATEST_ON");
                        newWallMaterial.EnableKeyword("_ALPHABLEND_ON");
                        newWallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        newWallMaterial.renderQueue = 3000;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Error setting up material properties: {e.Message}");
                    }
                }
            }
                
            // Save the material as an asset
            try
            {
                AssetDatabase.CreateAsset(newWallMaterial, "Assets/Materials/WallMaterial.mat");
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save wall material: {e.Message}");
            }
            
            // Wall Colorizer
            GameObject colorizerObj = new GameObject("Wall Colorizer");
            colorizerObj.transform.SetParent(mlSystem.transform);
            WallColorizer wallColorizer = colorizerObj.AddComponent<WallColorizer>();
            
            // We'll set up component references in the SetupComponentReferences method
            // after all objects are created
            
            // ARML Controller
            GameObject armlControllerObj = new GameObject("ARML Controller");
            armlControllerObj.transform.SetParent(mlSystem.transform);
            ARMLController armlController = armlControllerObj.AddComponent<ARMLController>();
            
            // Add FixARMLController component
            FixARMLController fixController = armlControllerObj.AddComponent<FixARMLController>();
            
            // We'll set up component references later in SetupComponentReferences
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in SetupMLComponents: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private static GameObject SetupUI()
    {
        try {
            // Create Canvas for UI
            GameObject canvasObj = new GameObject("UI Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // Create RawImage for AR display - ВАЖНО: создаем это с определенным именем
            GameObject displayObj = new GameObject("AR Display");
            displayObj.transform.SetParent(canvasObj.transform, false);
            displayObj.transform.SetAsFirstSibling(); // Размещаем позади всех UI элементов
            
            RectTransform displayRect = displayObj.AddComponent<RectTransform>();
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = Vector2.zero;
            displayRect.offsetMax = Vector2.zero;
            
            RawImage rawImage = displayObj.AddComponent<RawImage>();
            // Устанавливаем дефолтную текстуру
            rawImage.texture = Texture2D.blackTexture;
            
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
    
    // Create status text UI element
    private static void CreateStatusText(Transform parent)
    {
        try
        {
            GameObject statusObj = new GameObject("Status Text");
            statusObj.transform.SetParent(parent, false);
            
            RectTransform rect = statusObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0.85f);
            rect.anchorMax = new Vector2(1, 0.95f);
            rect.offsetMin = new Vector2(20, 0);
            rect.offsetMax = new Vector2(-20, 0);
            
            Text statusText = statusObj.AddComponent<Text>();
            statusText.text = "AR Wall Detection";
            
            // Safely get the font and handle null case
            Font defaultFont = null;
            try {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            } catch (System.Exception e) {
                Debug.LogWarning($"Could not load default font: {e.Message}. Using Arial if available.");
                defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
            }
            
            // Use a fallback if the font is still null
            if (defaultFont == null) {
                Debug.LogWarning("Creating empty font to avoid null references");
                defaultFont = new Font();
            }
            
            statusText.font = defaultFont;
            statusText.fontSize = 24;
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.MiddleCenter;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating status text: {e.Message}");
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
    
    // Create UI button with text
    private static GameObject CreateButton(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax)
    {
        try
        {
            GameObject buttonObj = new GameObject(name + " Button");
            buttonObj.transform.SetParent(parent, false);
            
            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 1);
            
            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1);
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1);
            colors.selectedColor = new Color(0.25f, 0.25f, 0.25f, 1);
            button.colors = colors;
            
            // Add text to button
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            Text buttonText = textObj.AddComponent<Text>();
            buttonText.text = text;
            
            // Safely get the font and handle null case
            Font defaultFont = null;
            try {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            } catch (System.Exception e) {
                Debug.LogWarning($"Could not load default font: {e.Message}. Using Arial if available.");
                defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
            }
            
            // Use a fallback if the font is still null
            if (defaultFont == null) {
                Debug.LogWarning("Creating empty font to avoid null references");
                defaultFont = new Font();
            }
            
            buttonText.font = defaultFont;
            buttonText.fontSize = 20;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            
            return buttonObj;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating button '{name}': {e.Message}");
            return new GameObject(name + "_error");
        }
    }
    
    // Connect all components and set up button actions
    private static void SetupComponentReferences(GameObject uiCanvas)
    {
        try
        {
            // Find key components
            ARMLController armlController = GameObject.Find("ARML Controller")?.GetComponent<ARMLController>();
            MLManager mlManager = GameObject.Find("ML Manager")?.GetComponent<MLManager>();
            ARManager arManager = GameObject.Find("AR Manager")?.GetComponent<ARManager>();
            DeepLabPredictor predictor = GameObject.Find("DeepLab Predictor")?.GetComponent<DeepLabPredictor>();
            EnhancedDeepLabPredictor enhancedPredictor = GameObject.Find("Enhanced DeepLab Predictor")?.GetComponent<EnhancedDeepLabPredictor>();
            WallColorizer wallColorizer = GameObject.Find("Wall Colorizer")?.GetComponent<WallColorizer>();
            ARSession arSession = GameObject.Find("AR Session")?.GetComponent<ARSession>();
            
            // Find our new AR wall anchoring components
            RemaluxARWallSetup arWallSetup = GameObject.Find("Remalux AR Wall System")?.GetComponent<RemaluxARWallSetup>();
            ARAwareWallMeshRenderer arWallRenderer = GameObject.FindObjectOfType<ARAwareWallMeshRenderer>();
            WallAnchorConnector wallAnchorConnector = GameObject.FindObjectOfType<WallAnchorConnector>();
            ARWallAnchor arWallAnchor = GameObject.FindObjectOfType<ARWallAnchor>();
            
            // Find UI elements - make this more robust
            RawImage displayImage = null;
            
            // First check if we have the UI canvas reference
            if (uiCanvas != null)
            {
                // Find display using transform hierarchy to be more reliable
                Transform displayTransform = uiCanvas.transform.Find("AR Display");
                if (displayTransform != null)
                {
                    displayImage = displayTransform.GetComponent<RawImage>();
                }
            }
            else
            {
                // Fallback - try to find by name if canvas reference is missing
                displayImage = GameObject.Find("AR Display")?.GetComponent<RawImage>();
            }
            
            // Find the material asset
            Material existingWallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMaterial.mat");
            
            // Set up ARMLController references to ARManager
            if (armlController != null && arManager != null)
            {
                SerializedObject serializedArmlController = new SerializedObject(armlController);
                var arManagerProp = serializedArmlController.FindProperty("arManager");
                if (arManagerProp != null)
                {
                    arManagerProp.objectReferenceValue = arManager;
                    Debug.Log("Set ARManager reference in ARMLController");
                }
                serializedArmlController.ApplyModifiedProperties();
            }
            
            // Connect ARWallAnchor with our WallAnchorConnector
            if (arWallAnchor != null && wallAnchorConnector != null && enhancedPredictor != null)
            {
                wallAnchorConnector.WallAnchor = arWallAnchor;
                wallAnchorConnector.Predictor = enhancedPredictor;
                
                // Get camera reference
                Camera mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    mainCamera = GameObject.Find("AR Camera")?.GetComponent<Camera>();
                }
                
                // Set camera reference
                if (mainCamera != null)
                {
                    wallAnchorConnector.ARCamera = mainCamera;
                }
                
                // Set ARPlaneManager reference
                ARPlaneManager planeManager = GameObject.FindObjectOfType<ARPlaneManager>();
                if (planeManager != null)
                {
                    wallAnchorConnector.ARPlaneManager = planeManager;
                    Debug.Log("Connected WallAnchorConnector with ARPlaneManager");
                }
                
                // Set ARAnchorManager reference
                ARAnchorManager anchorManager = GameObject.FindObjectOfType<ARAnchorManager>();
                if (anchorManager != null)
                {
                    wallAnchorConnector.AnchorManager = anchorManager;
                    Debug.Log("Connected WallAnchorConnector with ARAnchorManager");
                }
                
                Debug.Log("Connected WallAnchorConnector with ARWallAnchor and EnhancedDeepLabPredictor");
            }
            
            // Connect ARAwareWallMeshRenderer with our system
            if (arWallRenderer != null && enhancedPredictor != null && existingWallMaterial != null)
            {
                arWallRenderer.Predictor = enhancedPredictor;
                arWallRenderer.WallMaterial = existingWallMaterial;
                
                // Connect with AR components
                if (arWallAnchor != null)
                {
                    arWallRenderer.WallAnchor = arWallAnchor;
                }
                
                // Get camera manager
                ARCameraManager cameraManager = GameObject.FindObjectOfType<ARCameraManager>();
                if (cameraManager != null)
                {
                    arWallRenderer.CameraManager = cameraManager;
                }
                
                // Get raycast manager
                ARRaycastManager raycastManager = GameObject.FindObjectOfType<ARRaycastManager>();
                if (raycastManager != null)
                {
                    arWallRenderer.RaycastManager = raycastManager;
                }
                
                // Get plane manager
                ARPlaneManager planeManager = GameObject.FindObjectOfType<ARPlaneManager>();
                if (planeManager != null)
                {
                    arWallRenderer.PlaneManager = planeManager;
                }
                
                Debug.Log("Connected ARAwareWallMeshRenderer with all required AR components");
            }
            
            // Set up WallColorizer references
            if (wallColorizer != null)
            {
                SerializedObject serializedColorizer = new SerializedObject(wallColorizer);
                
                // Set display image reference
                var displayProp = serializedColorizer.FindProperty("displayImage");
                if (displayProp != null && displayImage != null)
                {
                    displayProp.objectReferenceValue = displayImage;
                    Debug.Log($"WallColorizer: Display image reference set to {displayImage.name}");
                }
                
                // Set camera reference
                var cameraProp = serializedColorizer.FindProperty("arCamera");
                if (cameraProp != null)
                {
                    Camera mainCamera = Camera.main;
                    if (mainCamera == null)
                    {
                        // Try to find AR Camera directly if main camera reference fails
                        mainCamera = GameObject.Find("AR Camera")?.GetComponent<Camera>();
                    }
                    
                    if (mainCamera != null)
                    {
                        cameraProp.objectReferenceValue = mainCamera;
                    }
                }
                
                // Set material reference
                var materialProp = serializedColorizer.FindProperty("wallMaterial");
                if (materialProp != null && existingWallMaterial != null)
                {
                    materialProp.objectReferenceValue = existingWallMaterial;
                }
                
                // Set color and opacity
                var colorProp = serializedColorizer.FindProperty("currentColor");
                if (colorProp != null)
                {
                    colorProp.colorValue = new Color(0.5f, 0.8f, 1f, 1f);
                }
                
                var opacityProp = serializedColorizer.FindProperty("wallOpacity");
                if (opacityProp != null)
                {
                    opacityProp.floatValue = 0.7f;
                }
                
                // Make sure to apply the properties
                serializedColorizer.ApplyModifiedProperties();
                EditorUtility.SetDirty(wallColorizer);
            }
            
            // Connect ARWallAnchor to our wall detection system so it can properly anchor
            // wall meshes created by ARAwareWallMeshRenderer
            if (arWallAnchor != null && arWallRenderer != null && arWallRenderer.WallAnchor == null)
            {
                arWallRenderer.WallAnchor = arWallAnchor;
                Debug.Log("Connected ARWallAnchor to ARAwareWallMeshRenderer for proper anchoring");
            }
                        
            // Set up color buttons to change wall color
            Transform colorPalette = null;
            Transform colorPanel = uiCanvas.transform.Find("Color Panel");
            if (colorPanel != null)
            {
                colorPalette = colorPanel.transform;
            }
            else
            {
                // Fallback - try to find by name
                colorPalette = GameObject.Find("Color Palette")?.transform;
            }
            
            // Connect color buttons to both our wall rendering systems (old and new)
            if (colorPalette != null)
            {
                foreach (Transform child in colorPalette)
                {
                    Button colorButton = child.GetComponent<Button>();
                    if (colorButton != null)
                    {
                        try
                        {
                            // Parse color from button name
                            string[] colorComponents = child.name.Split('_');
                            if (colorComponents.Length >= 4)
                            {
                                if (float.TryParse(colorComponents[1], out float r) &&
                                    float.TryParse(colorComponents[2], out float g) &&
                                    float.TryParse(colorComponents[3], out float b))
                                {
                                    Color buttonColor = new Color(r, g, b, 1f);
                                    
                                    // Add color selection action to ARML controller for old system
                                    if (armlController != null)
                                    {
                                        SerializedObject serializedButton = new SerializedObject(colorButton);
                                        if (serializedButton == null) continue;
                                        
                                        SerializedProperty onClick = serializedButton.FindProperty("m_OnClick");
                                        if (onClick == null) continue;
                                        
                                        // Clear any existing listeners
                                        SerializedProperty calls = onClick.FindPropertyRelative("m_PersistentCalls.m_Calls");
                                        if (calls == null) continue;
                                        
                                        calls.ClearArray();
                                        
                                        // Add a new persistent call
                                        calls.arraySize = 1;
                                        SerializedProperty call = calls.GetArrayElementAtIndex(0);
                                        if (call == null) continue;
                                        
                                        var targetProp = call.FindPropertyRelative("m_Target");
                                        var methodNameProp = call.FindPropertyRelative("m_MethodName");
                                        var modeProp = call.FindPropertyRelative("m_Mode");
                                        
                                        if (targetProp == null || methodNameProp == null || modeProp == null) continue;
                                        
                                        targetProp.objectReferenceValue = armlController;
                                        methodNameProp.stringValue = "SetWallColor";
                                        modeProp.enumValueIndex = 2; // ColorArgument mode
                                        
                                        // Set up argument object
                                        SerializedProperty arguments = call.FindPropertyRelative("m_Arguments");
                                        if (arguments == null) continue;
                                        
                                        var typeProp = arguments.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName");
                                        var colorProp = arguments.FindPropertyRelative("m_ColorArgument");
                                        
                                        if (typeProp == null || colorProp == null) continue;
                                        
                                        typeProp.stringValue = "UnityEngine.Color, UnityEngine";
                                        colorProp.colorValue = buttonColor;
                                        
                                        serializedButton.ApplyModifiedProperties();
                                        EditorUtility.SetDirty(colorButton);
                                        
                                        // Also connect to ARAwareWallMeshRenderer for the new system
                                        if (arWallRenderer != null)
                                        {
                                            // Create a UnityEvent that will call SetWallColor on arWallRenderer
                                            colorButton.onClick.AddListener(() => {
                                                arWallRenderer.SetWallColor(buttonColor);
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to set up color button: {e.Message}");
                        }
                    }
                }
            }
            
            Debug.Log("Component references have been set up successfully");
            
            // Find the XROrigin to add required components
            XROrigin xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                // Find or create the mesh manager GameObject
                Transform meshManagerTransform = xrOrigin.transform.Find("AR Mesh Manager");
                GameObject meshManagerObj;
                
                if (meshManagerTransform == null)
                {
                    // Create if it doesn't exist
                    meshManagerObj = new GameObject("AR Mesh Manager");
                    meshManagerObj.transform.SetParent(xrOrigin.transform);
                }
                else
                {
                    meshManagerObj = meshManagerTransform.gameObject;
                }
                
                // Ensure it has an ARMeshManager component
                ARMeshManager arMeshManager = meshManagerObj.GetComponent<ARMeshManager>();
                if (arMeshManager == null)
                {
                    arMeshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
                    arMeshManager.density = 0.5f;
                }
                
                // Make sure WallAligner is already attached
                WallAligner wallAligner = meshManagerObj.GetComponent<WallAligner>();
                if (wallAligner == null)
                {
                    wallAligner = SafeAddComponent<WallAligner>(meshManagerObj);
                    
                    // Set wall material
                    SerializedObject serializedWallAligner = new SerializedObject(wallAligner);
                    var wallMaterialProp = serializedWallAligner.FindProperty("wallMaterial");
                    if (wallMaterialProp != null && existingWallMaterial != null)
                    {
                        wallMaterialProp.objectReferenceValue = existingWallMaterial;
                        Debug.Log("Set wall material for WallAligner component");
                    }
                    serializedWallAligner.ApplyModifiedProperties();
                }
                
                // Disable WallMeshRenderer if it exists (we'll use ARAwareWallMeshRenderer instead)
                WallMeshRenderer wallMeshRenderer = meshManagerObj.GetComponent<WallMeshRenderer>();
                if (wallMeshRenderer != null)
                {
                    wallMeshRenderer.enabled = false;
                    Debug.Log("Disabled WallMeshRenderer to avoid conflicts with ARAwareWallMeshRenderer");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up component references: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Создает и настраивает улучшенную систему обнаружения стен
    /// </summary>
    private static void SetupWallDetectionSystem()
    {
        try
        {
            // Создаем основной объект Wall System
            GameObject wallSystem = new GameObject("Wall System");
            
            // Добавляем компоненты
            WallOptimizer wallOptimizer = wallSystem.AddComponent<WallOptimizer>();
            EnhancedWallRenderer enhancedRenderer = wallSystem.AddComponent<EnhancedWallRenderer>();
            
            // Отключаем отображение панели настройки стен
            WallDetectionTuner tuner = wallSystem.AddComponent<WallDetectionTuner>();
            if (tuner != null)
            {
                tuner.wallOptimizer = wallOptimizer;
                tuner.wallRenderer = enhancedRenderer;
                tuner.showTunerPanel = false; // Отключаем показ панели настройки
            }
            
            // Настраиваем параметры WallOptimizer
            if (wallOptimizer != null)
            {
                wallOptimizer.wallClassId = 15; // стандартный класс "wall" в модели
                wallOptimizer.confidenceThreshold = 0.4f; // более строгий порог уверенности
                wallOptimizer.minContourArea = 3000f; // минимальная площадь контура в пикселях
                wallOptimizer.minAspectRatio = 0.3f; // минимальное соотношение сторон
                wallOptimizer.maxAspectRatio = 4.0f; // максимальное соотношение сторон
                wallOptimizer.useMorphology = true; // применять морфологические операции
                wallOptimizer.morphKernelSize = 3; // размер ядра для морфологии
                wallOptimizer.minWallArea = 1.5f; // минимальная площадь стены в метрах
                wallOptimizer.wallMergeDistance = 0.5f; // расстояние для объединения близких стен
                wallOptimizer.showDebugInfo = false; // Отключаем отладочную информацию
            }
            
            // Настраиваем параметры EnhancedWallRenderer
            if (enhancedRenderer != null)
            {
                // Настраиваем ссылки на компоненты
                enhancedRenderer.ARCameraManager = UnityEngine.Object.FindAnyObjectByType<ARCameraManager>();
                enhancedRenderer.Predictor = UnityEngine.Object.FindAnyObjectByType<EnhancedDeepLabPredictor>();
                
                // Настраиваем визуальные параметры
                enhancedRenderer.WallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f); // цвет стен
                enhancedRenderer.WallOpacity = 0.7f; // прозрачность стен
                
                // Настраиваем параметры фильтрации
                enhancedRenderer.MinWallArea = 1.5f; // минимальная площадь стены
                enhancedRenderer.VerticalThreshold = 0.6f; // порог вертикальности
                enhancedRenderer.WallConfidenceThreshold = 0.4f; // порог уверенности
                enhancedRenderer.WallClassId = 15; // ID класса стены в модели
                enhancedRenderer.ShowDebugInfo = false; // Отключаем отладочную информацию
            }
            
            Debug.Log("Wall Detection System setup completed successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up Wall Detection System: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Создает слайдер для настройки числового параметра
    /// </summary>
    private static GameObject CreateSlider(GameObject parent, string label, float minValue, float maxValue, float defaultValue, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject sliderObj = new GameObject(label + " Slider");
        sliderObj.transform.SetParent(parent.transform, false);
        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchorMin = anchorMin;
        sliderRect.anchorMax = anchorMax;
        sliderRect.offsetMin = Vector2.zero;
        sliderRect.offsetMax = Vector2.zero;
        
        // Добавляем лейбл
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(sliderObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0.3f, 1);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        
        // Добавляем сам слайдер
        GameObject sliderControl = new GameObject("Slider Control");
        sliderControl.transform.SetParent(sliderObj.transform, false);
        RectTransform sliderControlRect = sliderControl.AddComponent<RectTransform>();
        sliderControlRect.anchorMin = new Vector2(0.31f, 0.5f);
        sliderControlRect.anchorMax = new Vector2(0.8f, 0.8f);
        sliderControlRect.offsetMin = Vector2.zero;
        sliderControlRect.offsetMax = Vector2.zero;
        
        Slider slider = sliderControl.AddComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = defaultValue;
        
        // Фон слайдера
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderControl.transform, false);
        RectTransform backgroundRect = background.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 1);
        
        // Заполнение слайдера
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderControl.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);
        
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.3f, 0.6f, 1f, 1);
        
        slider.fillRect = fillRect;
        
        // Ползунок слайдера
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(sliderControl.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0, 0.5f);
        handleRect.anchorMax = new Vector2(0, 0.5f);
        handleRect.sizeDelta = new Vector2(20, 20);
        
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(1f, 1f, 1f, 1);
        
        slider.handleRect = handleRect;
        
        // Добавляем отображение текущего значения
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(sliderObj.transform, false);
        RectTransform valueRect = valueObj.AddComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0.81f, 0.5f);
        valueRect.anchorMax = new Vector2(1, 1);
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        
        Text valueText = valueObj.AddComponent<Text>();
        valueText.text = defaultValue.ToString("F2");
        valueText.fontSize = 14;
        valueText.alignment = TextAnchor.MiddleRight;
        valueText.color = Color.white;
        
        // Обновление текста значения при изменении слайдера
        slider.onValueChanged.AddListener((float val) => {
            if (minValue >= 100)
                valueText.text = val.ToString("F0");
            else
                valueText.text = val.ToString("F2");
        });
        
        return sliderObj;
    }
    
    /// <summary>
    /// Создает переключатель (checkbox)
    /// </summary>
    private static GameObject CreateToggle(GameObject parent, string label, bool defaultValue, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject toggleObj = new GameObject(label + " Toggle");
        toggleObj.transform.SetParent(parent.transform, false);
        RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
        toggleRect.anchorMin = anchorMin;
        toggleRect.anchorMax = anchorMax;
        toggleRect.offsetMin = Vector2.zero;
        toggleRect.offsetMax = Vector2.zero;
        
        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = defaultValue;
        
        // Создаем фон для переключателя
        GameObject background = new GameObject("Background");
        background.transform.SetParent(toggleObj.transform, false);
        RectTransform backgroundRect = background.AddComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0, 0.5f);
        backgroundRect.anchorMax = new Vector2(0, 0.5f);
        backgroundRect.sizeDelta = new Vector2(20, 20);
        
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 1);
        
        // Создаем маркер выбора
        GameObject checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(background.transform, false);
        RectTransform checkmarkRect = checkmark.AddComponent<RectTransform>();
        checkmarkRect.anchorMin = Vector2.zero;
        checkmarkRect.anchorMax = Vector2.one;
        checkmarkRect.offsetMin = new Vector2(4, 4);
        checkmarkRect.offsetMax = new Vector2(-4, -4);
        
        Image checkmarkImage = checkmark.AddComponent<Image>();
        checkmarkImage.color = new Color(0.3f, 0.6f, 1f, 1);
        
        toggle.graphic = checkmarkImage;
        toggle.targetGraphic = backgroundImage;
        
        // Добавляем текст лейбла
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(toggleObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(25, 0);
        labelRect.offsetMax = Vector2.zero;
        
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        
        return toggleObj;
    }
    
    /// <summary>
    /// Создает выпадающий список
    /// </summary>
    private static GameObject CreateDropdown(GameObject parent, string label, int defaultValue, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject dropdownObj = new GameObject(label + " Dropdown");
        dropdownObj.transform.SetParent(parent.transform, false);
        RectTransform dropdownRect = dropdownObj.AddComponent<RectTransform>();
        dropdownRect.anchorMin = anchorMin;
        dropdownRect.anchorMax = anchorMax;
        dropdownRect.offsetMin = Vector2.zero;
        dropdownRect.offsetMax = Vector2.zero;
        
        // Добавляем лейбл
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0.3f, 1);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        
        // Добавляем сам дропдаун
        GameObject dropdownControl = new GameObject("Dropdown Control");
        dropdownControl.transform.SetParent(dropdownObj.transform, false);
        RectTransform dropdownControlRect = dropdownControl.AddComponent<RectTransform>();
        dropdownControlRect.anchorMin = new Vector2(0.31f, 0.2f);
        dropdownControlRect.anchorMax = new Vector2(1f, 0.8f);
        dropdownControlRect.offsetMin = Vector2.zero;
        dropdownControlRect.offsetMax = Vector2.zero;
        
        Image dropdownBg = dropdownControl.AddComponent<Image>();
        dropdownBg.color = new Color(0.2f, 0.2f, 0.2f, 1);
        
        Dropdown dropdown = dropdownControl.AddComponent<Dropdown>();
        
        // Создаем элементы дропдауна
        GameObject labelTextObj = new GameObject("Label");
        labelTextObj.transform.SetParent(dropdownControl.transform, false);
        RectTransform labelTextRect = labelTextObj.AddComponent<RectTransform>();
        labelTextRect.anchorMin = Vector2.zero;
        labelTextRect.anchorMax = Vector2.one;
        labelTextRect.offsetMin = new Vector2(10, 0);
        labelTextRect.offsetMax = new Vector2(-20, 0);
        
        Text dropdownLabelText = labelTextObj.AddComponent<Text>();
        dropdownLabelText.text = "Опция";
        dropdownLabelText.fontSize = 14;
        dropdownLabelText.alignment = TextAnchor.MiddleLeft;
        dropdownLabelText.color = Color.white;
        
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropdownControl.transform, false);
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0.5f);
        arrowRect.anchorMax = new Vector2(1, 0.5f);
        arrowRect.sizeDelta = new Vector2(20, 20);
        arrowRect.anchoredPosition = new Vector2(-10, 0);
        
        Image arrowImage = arrowObj.AddComponent<Image>();
        arrowImage.color = Color.white;
        
        // Настраиваем выпадающий список
        dropdown.targetGraphic = dropdownBg;
        dropdown.captionText = dropdownLabelText;
        
        // Добавляем опции
        dropdown.options.Clear();
        for (int i = 0; i <= 20; i++)
        {
            dropdown.options.Add(new Dropdown.OptionData($"Класс {i}"));
        }
        
        dropdown.value = defaultValue;
        dropdown.RefreshShownValue();
        
        return dropdownObj;
    }
    
    /// <summary>
    /// Создает кнопку с заданным текстом
    /// </summary>
    private static GameObject CreateUIButton(GameObject parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject buttonObj = new GameObject(name + " Button");
        buttonObj.transform.SetParent(parent.transform, false);
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.anchorMin = anchorMin;
        buttonRect.anchorMax = anchorMax;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        
        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 1);
        
        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        button.colors = colors;
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Text buttonText = textObj.AddComponent<Text>();
        buttonText.text = text;
        buttonText.fontSize = 14;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;
        
        return buttonObj;
    }

    /// <summary>
    /// Создает панель настроек обнаружения и рендеринга стен
    /// </summary>
    private static GameObject CreateWallSettingsPanel(GameObject parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        // Создаем панель настроек
        GameObject wallSettingsPanel = new GameObject("Wall Settings Panel");
        wallSettingsPanel.transform.SetParent(parent.transform, false);
        
        RectTransform panelRect = wallSettingsPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = anchorMin;
        panelRect.anchorMax = anchorMax;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Добавляем фон панели
        Image panelImage = wallSettingsPanel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        
        // Создаем заголовок
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(wallSettingsPanel.transform, false);
        
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.9f);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "Настройки обнаружения стен";
        titleText.fontSize = 18;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        
        // Создаем контент панели
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(wallSettingsPanel.transform, false);
        
        RectTransform contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 0.9f);
        contentRect.offsetMin = new Vector2(10, 10);
        contentRect.offsetMax = new Vector2(-10, -10);
        
        // Создаем группу настроек для WallDetectionOptimizer
        GameObject optimizerSettingsObj = new GameObject("Optimizer Settings");
        optimizerSettingsObj.transform.SetParent(contentObj.transform, false);
        
        RectTransform optimizerRect = optimizerSettingsObj.AddComponent<RectTransform>();
        optimizerRect.anchorMin = new Vector2(0, 0.7f);
        optimizerRect.anchorMax = new Vector2(1, 1);
        optimizerRect.offsetMin = Vector2.zero;
        optimizerRect.offsetMax = Vector2.zero;
        
        // Добавляем подзаголовок для настроек оптимизатора
        GameObject optimizerTitleObj = new GameObject("Optimizer Title");
        optimizerTitleObj.transform.SetParent(optimizerSettingsObj.transform, false);
        
        RectTransform optimizerTitleRect = optimizerTitleObj.AddComponent<RectTransform>();
        optimizerTitleRect.anchorMin = new Vector2(0, 0.8f);
        optimizerTitleRect.anchorMax = new Vector2(1, 1);
        optimizerTitleRect.offsetMin = Vector2.zero;
        optimizerTitleRect.offsetMax = Vector2.zero;
        
        Text optimizerTitleText = optimizerTitleObj.AddComponent<Text>();
        optimizerTitleText.text = "Параметры оптимизации";
        optimizerTitleText.fontSize = 16;
        optimizerTitleText.alignment = TextAnchor.MiddleLeft;
        optimizerTitleText.color = new Color(0.8f, 0.8f, 1f);
        
        // Создаем контент настроек оптимизатора
        GameObject optimizerContentObj = new GameObject("Optimizer Content");
        optimizerContentObj.transform.SetParent(optimizerSettingsObj.transform, false);
        
        RectTransform optimizerContentRect = optimizerContentObj.AddComponent<RectTransform>();
        optimizerContentRect.anchorMin = new Vector2(0, 0);
        optimizerContentRect.anchorMax = new Vector2(1, 0.8f);
        optimizerContentRect.offsetMin = Vector2.zero;
        optimizerContentRect.offsetMax = Vector2.zero;
        
        // Добавляем элементы управления для оптимизатора
        GameObject minConfidenceSlider = CreateSlider(optimizerContentObj, "Мин. уверенность", 0f, 1f, 0.8f, 
            new Vector2(0, 0.75f), new Vector2(1, 1f));
            
        GameObject minWallPercentSlider = CreateSlider(optimizerContentObj, "Мин. % стены", 0f, 1f, 0.6f, 
            new Vector2(0, 0.5f), new Vector2(1, 0.75f));
            
        GameObject minContourAreaSlider = CreateSlider(optimizerContentObj, "Мин. площадь", 100f, 5000f, 500f, 
            new Vector2(0, 0.25f), new Vector2(1, 0.5f));
            
        GameObject useAspectRatioToggle = CreateToggle(optimizerContentObj, "Проверять соотношение сторон", true, 
            new Vector2(0, 0), new Vector2(1, 0.25f));
            
        // Создаем группу настроек для WallMeshRenderer
        GameObject rendererSettingsObj = new GameObject("Renderer Settings");
        rendererSettingsObj.transform.SetParent(contentObj.transform, false);
        
        RectTransform rendererRect = rendererSettingsObj.AddComponent<RectTransform>();
        rendererRect.anchorMin = new Vector2(0, 0.35f);
        rendererRect.anchorMax = new Vector2(1, 0.65f);
        rendererRect.offsetMin = Vector2.zero;
        rendererRect.offsetMax = Vector2.zero;
        
        // Добавляем подзаголовок для настроек рендерера
        GameObject rendererTitleObj = new GameObject("Renderer Title");
        rendererTitleObj.transform.SetParent(rendererSettingsObj.transform, false);
        
        RectTransform rendererTitleRect = rendererTitleObj.AddComponent<RectTransform>();
        rendererTitleRect.anchorMin = new Vector2(0, 0.8f);
        rendererTitleRect.anchorMax = new Vector2(1, 1);
        rendererTitleRect.offsetMin = Vector2.zero;
        rendererTitleRect.offsetMax = Vector2.zero;
        
        Text rendererTitleText = rendererTitleObj.AddComponent<Text>();
        rendererTitleText.text = "Параметры отображения";
        rendererTitleText.fontSize = 16;
        rendererTitleText.alignment = TextAnchor.MiddleLeft;
        rendererTitleText.color = new Color(0.8f, 0.8f, 1f);
        
        // Создаем контент настроек рендерера
        GameObject rendererContentObj = new GameObject("Renderer Content");
        rendererContentObj.transform.SetParent(rendererSettingsObj.transform, false);
        
        RectTransform rendererContentRect = rendererContentObj.AddComponent<RectTransform>();
        rendererContentRect.anchorMin = new Vector2(0, 0);
        rendererContentRect.anchorMax = new Vector2(1, 0.8f);
        rendererContentRect.offsetMin = Vector2.zero;
        rendererContentRect.offsetMax = Vector2.zero;
        
        // Добавляем элементы управления для рендерера
        GameObject wallHeightSlider = CreateSlider(rendererContentObj, "Высота стены", 1f, 5f, 2.7f, 
            new Vector2(0, 0.75f), new Vector2(1, 1f));
            
        GameObject wallThicknessSlider = CreateSlider(rendererContentObj, "Толщина стены", 0.05f, 0.5f, 0.1f, 
            new Vector2(0, 0.5f), new Vector2(1, 0.75f));
            
        GameObject wallColorDropdown = CreateDropdown(rendererContentObj, "Цвет стены", 0, 
            new Vector2(0, 0.25f), new Vector2(1, 0.5f));
            
        GameObject showWireframeToggle = CreateToggle(rendererContentObj, "Показать каркас", false, 
            new Vector2(0, 0), new Vector2(1, 0.25f));
        
        // Создаем группу кнопок управления
        GameObject controlsObj = new GameObject("Controls");
        controlsObj.transform.SetParent(contentObj.transform, false);
        
        RectTransform controlsRect = controlsObj.AddComponent<RectTransform>();
        controlsRect.anchorMin = new Vector2(0, 0);
        controlsRect.anchorMax = new Vector2(1, 0.2f);
        controlsRect.offsetMin = Vector2.zero;
        controlsRect.offsetMax = Vector2.zero;
        
        // Создаем кнопки управления
        GameObject applyBtn = CreateUIButton(controlsObj, "Apply", "Применить", 
            new Vector2(0.6f, 0.25f), new Vector2(0.9f, 0.75f));
            
        GameObject resetBtn = CreateUIButton(controlsObj, "Reset", "Сбросить", 
            new Vector2(0.1f, 0.25f), new Vector2(0.4f, 0.75f));
        
        return wallSettingsPanel;
    }

    /// <summary>
    /// Создает основную панель настроек AR
    /// </summary>
    private static GameObject CreateARSettingsPanel()
    {
        // Создаем корневой объект для панели настроек
        GameObject settingsPanel = new GameObject("AR Settings Panel");
        settingsPanel.AddComponent<Canvas>();
        settingsPanel.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        settingsPanel.AddComponent<CanvasScaler>();
        settingsPanel.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        settingsPanel.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080, 1920);
        settingsPanel.AddComponent<GraphicRaycaster>();
        
        // Создаем фоновую панель
        GameObject backgroundPanel = new GameObject("Background Panel");
        backgroundPanel.transform.SetParent(settingsPanel.transform, false);
        
        RectTransform backgroundRect = backgroundPanel.AddComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0, 0);
        backgroundRect.anchorMax = new Vector2(1, 1);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        
        Image backgroundImage = backgroundPanel.AddComponent<Image>();
        backgroundImage.color = new Color(0, 0, 0, 0.5f);
        
        // Создаем основной контейнер
        GameObject mainContainer = new GameObject("Main Container");
        mainContainer.transform.SetParent(backgroundPanel.transform, false);
        
        RectTransform containerRect = mainContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.1f, 0.1f);
        containerRect.anchorMax = new Vector2(0.9f, 0.9f);
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;
        
        Image containerImage = mainContainer.AddComponent<Image>();
        containerImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        
        // Создаем заголовок
        GameObject titleObj = new GameObject("Main Title");
        titleObj.transform.SetParent(mainContainer.transform, false);
        
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.92f);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "Настройки AR сцены";
        titleText.fontSize = 24;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        
        // Создаем кнопку закрытия
        GameObject closeBtn = CreateUIButton(mainContainer, "CloseButton", "X", 
            new Vector2(0.95f, 0.95f), new Vector2(1, 1));
        
        // Создаем контейнер для вкладок
        GameObject tabContainer = new GameObject("Tab Container");
        tabContainer.transform.SetParent(mainContainer.transform, false);
        
        RectTransform tabContainerRect = tabContainer.AddComponent<RectTransform>();
        tabContainerRect.anchorMin = new Vector2(0, 0.85f);
        tabContainerRect.anchorMax = new Vector2(1, 0.92f);
        tabContainerRect.offsetMin = Vector2.zero;
        tabContainerRect.offsetMax = Vector2.zero;
        
        // Добавляем фон для вкладок
        Image tabContainerImage = tabContainer.AddComponent<Image>();
        tabContainerImage.color = new Color(0.3f, 0.3f, 0.3f, 1);
        
        // Создаем вкладки
        GameObject wallsTab = CreateUIButton(tabContainer, "WallsTab", "Стены", 
            new Vector2(0, 0), new Vector2(0.25f, 1));
        
        GameObject furnitureTab = CreateUIButton(tabContainer, "FurnitureTab", "Мебель", 
            new Vector2(0.25f, 0), new Vector2(0.5f, 1));
        
        GameObject measurementsTab = CreateUIButton(tabContainer, "MeasurementsTab", "Измерения", 
            new Vector2(0.5f, 0), new Vector2(0.75f, 1));
        
        GameObject settingsTab = CreateUIButton(tabContainer, "SettingsTab", "Настройки", 
            new Vector2(0.75f, 0), new Vector2(1, 1));
        
        // Создаем контейнер для содержимого вкладок
        GameObject contentContainer = new GameObject("Content Container");
        contentContainer.transform.SetParent(mainContainer.transform, false);
        
        RectTransform contentContainerRect = contentContainer.AddComponent<RectTransform>();
        contentContainerRect.anchorMin = new Vector2(0, 0);
        contentContainerRect.anchorMax = new Vector2(1, 0.85f);
        contentContainerRect.offsetMin = Vector2.zero;
        contentContainerRect.offsetMax = Vector2.zero;
        
        // Создаем панель настроек стен
        GameObject wallSettingsPanel = CreateWallSettingsPanel(contentContainer, 
            new Vector2(0, 0), new Vector2(1, 1));
        
        // TODO: Создать панели для других вкладок по мере необходимости
        
        return settingsPanel;
    }

    /// <summary>
    /// Creates the AR components for the scene
    /// </summary>
    /// <returns>The XR Origin GameObject</returns>
    private static GameObject CreateARComponents()
    {
        // Create the AR system with all necessary components
        GameObject arSystem = SetupARSystem();
        
        // Setup ML components for wall detection
        SetupMLComponents(arSystem);
        
        // Setup wall detection system
        SetupWallDetectionSystem();
        
        // Return the XR Origin to be used for reference
        return arSystem.transform.Find("XR Origin").gameObject;
    }
    
    /// <summary>
    /// Creates the AR UI Canvas with main UI elements
    /// </summary>
    /// <returns>The Canvas GameObject</returns>
    private static GameObject CreateARUICanvas()
    {
        // Create the main UI system with only color palette
        GameObject uiCanvas = SetupUI();
        
        // Setup component references
        SetupComponentReferences(uiCanvas);
        
        return uiCanvas;
    }
    
    /// <summary>
    /// Creates the tool buttons panel as part of the UI
    /// </summary>
    /// <param name="parentCanvas">The parent Canvas GameObject</param>
    /// <returns>The tool buttons panel GameObject</returns>
    private static GameObject CreateToolButtonsPanel(GameObject parentCanvas)
    {
        // Create a panel for the tool buttons
        GameObject toolPanel = new GameObject("Tool Buttons Panel");
        RectTransform toolPanelRect = toolPanel.AddComponent<RectTransform>();
        toolPanel.transform.SetParent(parentCanvas.transform, false);
        
        // Configure the panel position (bottom of the screen)
        toolPanelRect.anchorMin = new Vector2(0, 0);
        toolPanelRect.anchorMax = new Vector2(1, 0.2f);
        toolPanelRect.offsetMin = new Vector2(10, 10);
        toolPanelRect.offsetMax = new Vector2(-10, -10);
        
        // Add visual panel component
        Image panelImage = toolPanel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        
        // Create color selection buttons
        CreateColorButtons(toolPanel.transform);
        
        // Create tool buttons
        // Place buttons on the panel
        GameObject placeButton = CreateButton(toolPanel.transform, "PlaceButton", "Place", new Vector2(0.05f, 0.3f), new Vector2(0.23f, 0.9f));
        GameObject eraseButton = CreateButton(toolPanel.transform, "EraseButton", "Erase", new Vector2(0.28f, 0.3f), new Vector2(0.46f, 0.9f));
        GameObject moveButton = CreateButton(toolPanel.transform, "MoveButton", "Move", new Vector2(0.51f, 0.3f), new Vector2(0.69f, 0.9f));
        GameObject colorButton = CreateButton(toolPanel.transform, "ColorButton", "Color", new Vector2(0.74f, 0.3f), new Vector2(0.92f, 0.9f));
        
        return toolPanel;
    }
} 