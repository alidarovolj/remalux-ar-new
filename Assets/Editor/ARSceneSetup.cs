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

public class ARSceneSetup : Editor
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
    
    [MenuItem("AR Wall Detection/Setup Complete Scene")]
    public static void SetupARScene()
    {
        try
        {
            // Check Editor version for known issues
            Debug.Log($"Unity Editor Version: {Application.unityVersion}");
            
            // Force a garbage collection to help with memory issues
            System.GC.Collect();
            
            // Make sure the Scripts folder exists
            if (!Directory.Exists("Assets/Scripts"))
            {
                AssetDatabase.CreateFolder("Assets", "Scripts");
                AssetDatabase.Refresh();
                Debug.Log("Created Scripts folder");
            }
            
            // Create a new scene with minimal setup first
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log("Created new empty scene");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating new scene: {e.Message}");
                // Try to continue anyway with default setup
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            }
            
            // Create AR system components
            GameObject arSystem = null;
            try 
            {
                arSystem = SetupARSystem();
                Debug.Log("AR System setup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in AR System setup: {e.Message}\n{e.StackTrace}");
                throw; // Re-throw to abort the process
            }
            
            // Setup ML components
            try
            {
                SetupMLComponents(arSystem);
                Debug.Log("ML Components setup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in ML Components setup: {e.Message}\n{e.StackTrace}");
                // Continue with setup even if ML components fail
            }
            
            GameObject uiCanvas = null;
            try
            {
                uiCanvas = SetupUI();
                Debug.Log("UI setup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in UI setup: {e.Message}\n{e.StackTrace}");
                // Create a minimal canvas if UI setup fails
                uiCanvas = new GameObject("UI Canvas");
                uiCanvas.AddComponent<Canvas>();
                uiCanvas.AddComponent<CanvasScaler>();
                uiCanvas.AddComponent<GraphicRaycaster>();
            }
            
            // Connect components after all objects are created
            try
            {
                SetupComponentReferences(uiCanvas);
                Debug.Log("Component references setup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in component references setup: {e.Message}\n{e.StackTrace}");
            }
            
            // Save the scene
            try
            {
                string scenePath = "Assets/Scenes/WallDetectionSceneAuto.unity";
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
                Debug.Log("AR Wall Detection Scene has been created successfully and saved as " + scenePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving scene: {e.Message}\n{e.StackTrace}");
            }
            
            // Force a refresh of the Asset Database
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogError($"Critical error during scene setup: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Scene Setup Error", 
                "An error occurred while setting up the AR Wall Detection scene. Check the console for details.\n\n" +
                "Error: " + e.Message, "OK");
        }
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

            // Создаем child объект для ARMeshManager (должен быть child для XROrigin)
            GameObject meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOriginObj.transform);
            
            // Добавляем ARMeshManager на child объект безопасным способом
            ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
            meshManager.density = 0.5f;
            Debug.Log("Added ARMeshManager as child of XROrigin for mesh processing");

            // Добавляем Raycast Manager для определения поверхностей
            ARRaycastManager raycastManager = xrOriginObj.AddComponent<ARRaycastManager>();

            // Создаем и настраиваем ARManager
            GameObject arManagerObj = new GameObject("AR Manager");
            arManagerObj.transform.SetParent(arSystem.transform);
            ARManager arManager = arManagerObj.AddComponent<ARManager>();
            
            // Устанавливаем ссылки на необходимые компоненты
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

            // Добавляем наш SurfaceDetector
            SurfaceDetector surfaceDetector = xrOriginObj.AddComponent<SurfaceDetector>();

            // Добавляем PlaneVisualizer для отображения обнаруженных поверхностей
            PlaneVisualizer planeVisualizer = xrOriginObj.AddComponent<PlaneVisualizer>();

            // Создаем материал для визуализации плоскостей
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

            // Сохраняем материал в проекте
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
            wallDetectionSetup.createUIPanel = true;
            wallDetectionSetup.applyFixesToOriginal = true;
            wallDetectionSetup.initialThreshold = 0.3f;
            wallDetectionSetup.wallClassId = 9;
            wallDetectionSetup.useArgMaxMode = true;
            
            Debug.Log("Wall Detection System added to scene with enhanced recognition");

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
                
            Material wallMaterial;
            if (wallShader != null)
            {
                wallMaterial = new Material(wallShader);
                wallMaterial.SetColor("_Color", new Color(0.5f, 0.8f, 1f, 0.7f));
                
                // Use try-catch to avoid errors with missing properties
                try { wallMaterial.SetFloat("_Opacity", 0.7f); } catch { }
                try { wallMaterial.SetFloat("_Threshold", 0.03f); } catch { }
                try { wallMaterial.SetFloat("_SmoothFactor", 0.01f); } catch { }
                try { wallMaterial.SetFloat("_EdgeEnhance", 1.2f); } catch { }
            }
            else
            {
                // Fallback to standard transparent material
                wallShader = Shader.Find("Standard");
                if (wallShader == null)
                {
                    // Ultimate fallback - create a default material
                    wallMaterial = new Material(Shader.Find("Diffuse"));
                    wallMaterial.color = new Color(0.5f, 0.8f, 1f, 0.7f);
                }
                else
                {
                    wallMaterial = new Material(wallShader);
                    wallMaterial.color = new Color(0.5f, 0.8f, 1f, 0.7f);
                    
                    try
                    {
                        wallMaterial.SetFloat("_Mode", 3); // Transparent mode
                        wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        wallMaterial.SetInt("_ZWrite", 0);
                        wallMaterial.DisableKeyword("_ALPHATEST_ON");
                        wallMaterial.EnableKeyword("_ALPHABLEND_ON");
                        wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        wallMaterial.renderQueue = 3000;
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
                AssetDatabase.CreateAsset(wallMaterial, "Assets/Materials/WallMaterial.mat");
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
            
            // Create panel with buttons
            GameObject panelObj = new GameObject("Control Panel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(1, 0.2f);
            panelRect.offsetMin = new Vector2(10, 10);
            panelRect.offsetMax = new Vector2(-10, -10);
            
            Image panelImage = panelObj.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            
            // Create color palette
            CreateColorButtons(panelObj.transform);
            
            // Add status text
            CreateStatusText(canvasObj.transform);
            
            // Create RawImage for AR display - IMPORTANT: Create this with a specific name
            GameObject displayObj = new GameObject("AR Display");
            displayObj.transform.SetParent(canvasObj.transform, false);
            displayObj.transform.SetAsFirstSibling(); // Place behind all UI elements
            
            RectTransform displayRect = displayObj.AddComponent<RectTransform>();
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = Vector2.zero;
            displayRect.offsetMax = Vector2.zero;
            
            RawImage rawImage = displayObj.AddComponent<RawImage>();
            // Set a default texture to avoid null reference
            rawImage.texture = Texture2D.blackTexture;
            
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
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);
            
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
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMaterial.mat");
            
            // Check if essential components are missing and log errors
            bool isMissingComponents = false;
            if (armlController == null) { Debug.LogError("ARMLController is missing"); isMissingComponents = true; }
            if (mlManager == null) { Debug.LogError("MLManager is missing"); isMissingComponents = true; }
            if (predictor == null) { Debug.LogError("DeepLabPredictor is missing"); isMissingComponents = true; }
            if (wallColorizer == null) { Debug.LogError("WallColorizer is missing"); isMissingComponents = true; }
            if (displayImage == null) { Debug.LogError("AR Display RawImage is missing"); isMissingComponents = true; }
            
            if (isMissingComponents)
            {
                Debug.LogError("One or more required components are missing. Scene setup might be incomplete.");
                // Continue with setup for components that do exist
            }
            
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
            
            // Set up WallColorizer references FIRST to ensure it has display image
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
                else
                {
                    Debug.LogError("Failed to set display image reference for WallColorizer");
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
                    else
                    {
                        Debug.LogError("Failed to find AR Camera for WallColorizer");
                    }
                }
                
                // Set material reference
                var materialProp = serializedColorizer.FindProperty("wallMaterial");
                if (materialProp != null && wallMaterial != null)
                {
                    materialProp.objectReferenceValue = wallMaterial;
                }
                else
                {
                    Debug.LogWarning("Wall material not set for WallColorizer");
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
            
            // Set up ARMLController references
            if (armlController != null)
            {
                SerializedObject serializedArmlController = new SerializedObject(armlController);
                
                if (arManager != null)
                {
                    serializedArmlController.FindProperty("arManager").objectReferenceValue = arManager;
                }
                
                if (mlManager != null)
                {
                    serializedArmlController.FindProperty("mlManager").objectReferenceValue = mlManager;
                }
                
                if (predictor != null)
                {
                    serializedArmlController.FindProperty("deepLabPredictor").objectReferenceValue = predictor;
                }
                
                if (wallColorizer != null)
                {
                    serializedArmlController.FindProperty("wallColorizer").objectReferenceValue = wallColorizer;
                }
                
                var predIntervalProp = serializedArmlController.FindProperty("predictionInterval");
                if (predIntervalProp != null)
                {
                    predIntervalProp.floatValue = 0.5f;
                }
                
                serializedArmlController.ApplyModifiedProperties();
                EditorUtility.SetDirty(armlController);
            }
            
            // Set up FixARMLController references
            if (armlController != null)
            {
                FixARMLController fixController = armlController.GetComponent<FixARMLController>();
                if (fixController != null)
                {
                    SerializedObject serializedFixController = new SerializedObject(fixController);
                    
                    if (arSession != null)
                    {
                        serializedFixController.FindProperty("arSession").objectReferenceValue = arSession;
                    }
                    
                    serializedFixController.FindProperty("armlController").objectReferenceValue = armlController;
                    serializedFixController.ApplyModifiedProperties();
                    EditorUtility.SetDirty(fixController);
                }
                
                // Disable auto-start in ARMLController
                SerializedObject serializedController = new SerializedObject(armlController);
                var autoStartProp = serializedController.FindProperty("autoStartAR");
                if (autoStartProp != null)
                {
                    autoStartProp.boolValue = false;
                }
                serializedController.ApplyModifiedProperties();
            }
            
            // Set up color buttons to change wall color
            Transform colorPalette = GameObject.Find("Color Palette")?.transform;
            if (colorPalette != null && armlController != null)
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
                                    
                                    // Add color selection action - fix method signature
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
                
                // Add WallMeshRenderer to the same GameObject as ARMeshManager
                WallMeshRenderer wallMeshRenderer = meshManagerObj.GetComponent<WallMeshRenderer>();
                if (wallMeshRenderer == null)
                {
                    wallMeshRenderer = SafeAddComponent<WallMeshRenderer>(meshManagerObj);
                }
                
                if (wallMeshRenderer != null)
                {
                    // Get required references
                    ARCameraManager arCameraManager = UnityEngine.Object.FindAnyObjectByType<ARCameraManager>();
                    
                    // Сначала проверяем наличие EnhancedDeepLabPredictor
                    EnhancedDeepLabPredictor foundEnhancedPredictor = UnityEngine.Object.FindAnyObjectByType<EnhancedDeepLabPredictor>();
                    DeepLabPredictor basicPredictor = UnityEngine.Object.FindAnyObjectByType<DeepLabPredictor>();
                    
                    // Set up references
                    wallMeshRenderer.ARCameraManager = arCameraManager;
                    
                    // Предпочтительно использовать EnhancedDeepLabPredictor, но если его нет, 
                    // создаем его на основе базового предиктора
                    if (foundEnhancedPredictor != null)
                    {
                        wallMeshRenderer.Predictor = foundEnhancedPredictor;
                        Debug.Log("WallMeshRenderer configured with EnhancedDeepLabPredictor");
                    }
                    else if (basicPredictor != null)
                    {
                        // Создаем EnhancedDeepLabPredictor и копируем настройки из базового предиктора
                        GameObject enhancedPredictorObj = new GameObject("Enhanced DeepLab Predictor");
                        enhancedPredictorObj.transform.SetParent(basicPredictor.transform.parent);
                        EnhancedDeepLabPredictor newEnhancedPredictor = enhancedPredictorObj.AddComponent<EnhancedDeepLabPredictor>();
                        
                        // Копируем настройки
                        newEnhancedPredictor.modelAsset = basicPredictor.modelAsset;
                        newEnhancedPredictor.WallClassId = (byte)9;
                        newEnhancedPredictor.ClassificationThreshold = 0.5f;
                        newEnhancedPredictor.useArgMaxMode = true;
                        newEnhancedPredictor.debugMode = true;
                        
                        wallMeshRenderer.Predictor = newEnhancedPredictor;
                        Debug.Log("Created EnhancedDeepLabPredictor and configured WallMeshRenderer with it");
                    }
                    
                    // Configure wall detection settings
                    wallMeshRenderer.VerticalThreshold = 0.6f;
                    wallMeshRenderer.WallConfidenceThreshold = 0.2f;
                    wallMeshRenderer.ShowDebugInfo = true;
                    wallMeshRenderer.ShowAllMeshes = true; // Show all meshes for debugging
                    
                    // Create a material for wall visualization if needed
                    if (wallMeshRenderer.WallMaterial == null)
                    {
                        Material meshWallMaterial = new Material(Shader.Find("Standard"));
                        meshWallMaterial.color = new Color(0.3f, 0.5f, 0.8f, 0.5f);
                        wallMeshRenderer.WallMaterial = meshWallMaterial;
                    }
                    
                    Debug.Log("Wall mesh renderer configured successfully");
                }
            }
            else
            {
                Debug.LogWarning("XROrigin not found - cannot setup WallMeshRenderer");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up component references: {ex.Message}\n{ex.StackTrace}");
        }
    }
} 