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
using System.Collections.Generic;

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
        
        // Проверяем и исправляем позицию ARMeshManager для предотвращения ошибок
        FixARMeshManagerHierarchy();
        
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
    /// Исправляет положение AR Mesh Manager в текущей сцене
    /// Перемещает AR Mesh Manager из-под Camera Offset непосредственно под XR Origin
    /// </summary>
    [MenuItem("AR/Fix Mesh Manager Position")]
    public static void FixMeshManagerPosition()
    {
        // Используем универсальный метод для исправления иерархии ARMeshManager
        FixARMeshManagerHierarchy();
        
        // Сообщаем пользователю, что позиция была исправлена
        EditorUtility.DisplayDialog("Успех", "Проверка и исправление позиции AR Mesh Manager выполнены успешно. Теперь все ARMeshManager объекты размещены правильно под XROrigin.", "OK");
        
        // Предлагаем сохранить сцену
        if (EditorUtility.DisplayDialog("Сохранение", "Сохранить изменения сцены?", "Да", "Нет"))
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Сцена сохранена после исправления позиции AR Mesh Manager.");
        }
        else
        {
            Debug.Log("Сцена НЕ сохранена после исправления позиции AR Mesh Manager. Не забудьте сохранить вручную!");
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
    
    public static XROrigin SetupARSystem()
    {
        try
        {
            // Удаляем все старые ARMeshManager, чтобы не плодить дубли
            foreach (var old in GameObject.FindObjectsOfType<ARMeshManager>())
                GameObject.DestroyImmediate(old.gameObject);
            
            // Проверяем существование необходимых компонентов
            XROrigin xrOrigin = GameObject.FindObjectOfType<XROrigin>();
            UnityEngine.XR.ARFoundation.ARSession arSession = GameObject.FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
            
            // Создаем AR Session если его нет
            if (arSession == null)
            {
                GameObject arSessionObj = new GameObject("AR Session");
                arSession = arSessionObj.AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
                arSessionObj.AddComponent<UnityEngine.XR.ARFoundation.ARInputManager>();
                
                Debug.Log("Created AR Session");
            }
            
            // Создаем XR Origin если его нет
            GameObject xrOriginObj = null;
            if (xrOrigin == null)
            {
                // Создаем объект XR Origin
                xrOriginObj = new GameObject("XR Origin");
                xrOrigin = xrOriginObj.AddComponent<XROrigin>();
                
                // Настраиваем Camera Offset
                GameObject cameraOffsetObj = new GameObject("Camera Offset");
                cameraOffsetObj.transform.SetParent(xrOriginObj.transform);
                
                // Создаем AR Camera
                GameObject arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffsetObj.transform);
                
                // Настраиваем камеру
                Camera arCamera = arCameraObj.AddComponent<Camera>();
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                
                // Добавляем компоненты AR к камере
                arCameraObj.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
                arCameraObj.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
                
                // Настраиваем XROrigin
                xrOrigin.Camera = arCamera;
                xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
                
                Debug.Log("Created XR Origin with AR Camera");
            }
            else
            {
                xrOriginObj = xrOrigin.gameObject;
            }
            
            // Строго привязываем ARMeshManager к только что созданному XR Origin
            var meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            meshManagerObj.transform.localPosition = Vector3.zero;
            meshManagerObj.transform.localRotation = Quaternion.identity;
            meshManagerObj.transform.localScale = Vector3.one;
            ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
            meshManager.density = 0.5f;
            
            Debug.Log("Created AR Mesh Manager as child of XR Origin");
            
            // Добавляем недостающие компоненты для работы с AR
            if (xrOrigin != null && xrOrigin.gameObject.GetComponent<ARRaycastManager>() == null)
            {
                xrOrigin.gameObject.AddComponent<ARRaycastManager>();
                Debug.Log("Added AR Raycast Manager to XR Origin");
            }
            
            Debug.Log("AR System setup completed successfully");
            
            return xrOrigin;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up AR System: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    private static GameObject SetupMLComponents(GameObject parent)
    {
        try
        {
            if (parent == null)
            {
                Debug.LogError("Parent object is null. Cannot setup ML components.");
                return null;
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
        return parent;
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
                if (materialProp != null && wallMaterial != null)
                {
                    materialProp.objectReferenceValue = wallMaterial;
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
                
                // Make sure WallAligner is already attached
                WallAligner wallAligner = meshManagerObj.GetComponent<WallAligner>();
                if (wallAligner == null)
                {
                    wallAligner = SafeAddComponent<WallAligner>(meshManagerObj);
                    
                    // Set wall material
                    SerializedObject serializedWallAligner = new SerializedObject(wallAligner);
                    var wallMaterialProp = serializedWallAligner.FindProperty("wallMaterial");
                    if (wallMaterialProp != null && wallMaterial != null)
                    {
                        wallMaterialProp.objectReferenceValue = wallMaterial;
                        Debug.Log("Set wall material for WallAligner component");
                    }
                    serializedWallAligner.ApplyModifiedProperties();
                }
                
                // Disable WallMeshRenderer if it exists (we'll use WallAligner instead)
                WallMeshRenderer wallMeshRenderer = meshManagerObj.GetComponent<WallMeshRenderer>();
                if (wallMeshRenderer != null)
                {
                    wallMeshRenderer.enabled = false;
                    Debug.Log("Disabled WallMeshRenderer to avoid conflicts with WallAligner");
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
            // ВАЖНО: НЕ создаем ARMeshManager в этом методе, только проверяем существующий
            XROrigin xrOrigin = GameObject.FindObjectOfType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("[CRITICAL ERROR] XROrigin не найден! Невозможно настроить систему обнаружения стен.");
                return;
            }
            
            // Проверяем наличие ARMeshManager под XROrigin
            ARMeshManager meshManager = null;
            foreach (Transform child in xrOrigin.transform)
            {
                meshManager = child.GetComponent<ARMeshManager>();
                if (meshManager != null)
                {
                    Debug.Log($"Обнаружен ARMeshManager: {child.name}");
                    break;
                }
            }
            
            // Если ARMeshManager не найден - ТОЛЬКО проверяем, но не создаем новый
            // Создание должно происходить в методе SetupARSystem()
            if (meshManager == null)
            {
                Debug.LogWarning("[WARNING] ARMeshManager не найден под XROrigin! Обнаружение стен может работать некорректно.");
            }
            
            // Создаем основной объект Wall Detection System
            GameObject wallSystem = new GameObject("Wall Detection System");
            
            // Добавляем основной скрипт настройки
            WallDetectionSetup wallSetup = wallSystem.AddComponent<WallDetectionSetup>();
            wallSetup.autoSetup = true;
            wallSetup.useEnhancedPredictor = true;
            wallSetup.initialThreshold = 0.3f;
            wallSetup.wallClassId = 9; // ID класса стены в модели ADE20K
            
            // Добавляем WallMeshRenderer
            WallMeshRenderer meshRenderer = wallSystem.AddComponent<WallMeshRenderer>();
            if (meshRenderer != null)
            {
                // Настраиваем базовые параметры
                meshRenderer._wallClassId = 9;  // ADE20K wall class ID
                meshRenderer.WallConfidenceThreshold = 0.3f; // Используем публичное свойство
                meshRenderer.ARCameraManager = FindObjectOfType<ARCameraManager>();
                
                // Ищем EnhancedDeepLabPredictor в сцене
                meshRenderer.Predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
                
                // Отладочные настройки (отключаем для производства)
                meshRenderer.ShowDebugInfo = false;
            }
            
            // ФИНАЛЬНАЯ ПРОВЕРКА: ARMeshManager должен быть только под XROrigin
            ARMeshManager[] allMeshManagers = GameObject.FindObjectsOfType<ARMeshManager>();
            foreach (ARMeshManager manager in allMeshManagers)
            {
                if (manager != null && manager.transform.parent != xrOrigin.transform)
                {
                    Debug.LogError($"[CRITICAL ERROR] В SetupWallDetectionSystem обнаружен ARMeshManager ({manager.gameObject.name}) с неверным родителем!");
                    manager.transform.SetParent(xrOrigin.transform);
                    Debug.Log($"ARMeshManager перемещен под XROrigin!");
                }
            }
            
            // Добавляем WallOptimizer
            WallOptimizer wallOptimizer = wallSystem.AddComponent<WallOptimizer>();
            if (wallOptimizer != null)
            {
                // Настраиваем параметры
                wallOptimizer.wallClassId = 9; // класс "wall" в модели ADE20K
                wallOptimizer.confidenceThreshold = 0.3f;
                wallOptimizer.minContourArea = 3000f;
                wallOptimizer.minAspectRatio = 0.3f;
                wallOptimizer.maxAspectRatio = 4.0f;
                wallOptimizer.useMorphology = true;
                wallOptimizer.morphKernelSize = 3;
                wallOptimizer.minWallArea = 1.5f;
                wallOptimizer.wallMergeDistance = 0.5f;
                wallOptimizer.showDebugInfo = false;
                
                // Используем reflection для доступа к приватным полям
                try {
                    var meshRendererField = wallOptimizer.GetType().GetField("meshRenderer", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (meshRendererField != null) {
                        meshRendererField.SetValue(wallOptimizer, meshRenderer);
                        Debug.Log("ARSceneSetup: Set meshRenderer reference via reflection");
                    }
                    
                    var enhancedPredictorField = wallOptimizer.GetType().GetField("enhancedPredictor", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (enhancedPredictorField != null) {
                        var enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
                        enhancedPredictorField.SetValue(wallOptimizer, enhancedPredictor);
                        Debug.Log("ARSceneSetup: Set enhancedPredictor reference via reflection");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning($"ARSceneSetup: Failed to set private fields: {e.Message}");
                }
            }
            
            // Добавляем и настраиваем EnhancedWallRenderer
            EnhancedWallRenderer enhancedRenderer = wallSystem.AddComponent<EnhancedWallRenderer>();
            if (enhancedRenderer != null)
            {
                // Настраиваем ссылки на компоненты
                enhancedRenderer.ARCameraManager = FindObjectOfType<ARCameraManager>();
                enhancedRenderer.Predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
                
                // Настраиваем визуальные параметры
                enhancedRenderer.WallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
                enhancedRenderer.WallOpacity = 0.7f;
                
                // Настраиваем параметры фильтрации
                enhancedRenderer.MinWallArea = 1.5f;
                enhancedRenderer.VerticalThreshold = 0.6f;
                enhancedRenderer.WallConfidenceThreshold = 0.3f;
                enhancedRenderer.WallClassId = 9; // ID класса стены в модели ADE20K
                enhancedRenderer.ShowDebugInfo = false;
            }
            
            Debug.Log("Wall Detection System setup completed successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up Wall Detection System: {ex.Message}\n{ex.StackTrace}");
        }
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
            XROrigin xrOrigin = GameObject.FindObjectOfType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("ОШИБКА: XROrigin не найден в сцене! Невозможно исправить иерархию.");
                return;
            }
            
            // Получаем все ARMeshManager в сцене, которые НЕ являются детьми XROrigin
            List<ARMeshManager> invalidManagers = new List<ARMeshManager>();
            
            ARMeshManager[] allManagers = GameObject.FindObjectsOfType<ARMeshManager>();
            foreach (ARMeshManager meshManager in allManagers)
            {
                if (meshManager != null && meshManager.transform.parent != xrOrigin.transform)
                {
                    invalidManagers.Add(meshManager);
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
                    
                    ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
                    meshManager.density = 0.5f;
                }
                else
                {
                    Debug.Log("Все ARMeshManager уже имеют правильную иерархию.");
                }
                
                return;
            }
            
            Debug.LogWarning($"Найдено {invalidCount} ARMeshManager с неправильной иерархией.");
            
            // Удаляем все неправильные ARMeshManager
            foreach (ARMeshManager manager in invalidManagers)
            {
                Debug.LogWarning($"Удаляем ARMeshManager с неправильной иерархией: {manager.gameObject.name}");
                GameObject.DestroyImmediate(manager.gameObject);
            }
            
            // Создаем новый ARMeshManager под XROrigin, если его еще нет
            ARMeshManager existingMeshManager = xrOrigin.GetComponentInChildren<ARMeshManager>(true);
            if (existingMeshManager == null)
            {
                GameObject meshManagerObj = new GameObject("AR Mesh Manager");
                meshManagerObj.transform.SetParent(xrOrigin.transform, false);
                meshManagerObj.transform.localPosition = Vector3.zero;
                meshManagerObj.transform.localRotation = Quaternion.identity;
                meshManagerObj.transform.localScale = Vector3.one;
                
                ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
                meshManager.density = 0.5f;
                
                Debug.Log("Создан новый ARMeshManager как дочерний объект XROrigin");
            }
            
            Debug.Log("Исправление иерархии ARMeshManager завершено успешно");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Ошибка при исправлении иерархии ARMeshManager: {ex.Message}\n{ex.StackTrace}");
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
            var arSession = GameObject.FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
            if (arSession != null)
            {
                arSession.transform.SetParent(arSystem.transform, false);
                Debug.Log("AR Session added to AR System container");
            }
            
            // Setup ML components for wall detection
            SetupMLComponents(arSystem);
            
            // Setup wall detection system
            SetupWallDetectionSystem();
            
            // Return the container system
            return arSystem;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error creating AR components: {ex.Message}\n{ex.StackTrace}");
            return new GameObject("AR System (Error)");
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

    [MenuItem("AR/Fix AR Mesh Manager Hierarchy %#m")]
    public static void FixARMeshManagerForceUpdate()
    {
        Debug.Log("=== ЗАПУСК ИНСТРУМЕНТА ИСПРАВЛЕНИЯ AR MESH MANAGER ===");
        
        // Получаем все ARMeshManager в сцене
        ARMeshManager[] meshManagers = GameObject.FindObjectsOfType<ARMeshManager>();
        
        if (meshManagers.Length > 0)
        {
            Debug.Log($"Найдено {meshManagers.Length} ARMeshManager объектов. Исправляем...");
            
            // Принудительно удаляем все ARMeshManager, которые не под XROrigin
            foreach (ARMeshManager manager in meshManagers)
            {
                if (manager != null) 
                {
                    XROrigin parentOrigin = manager.GetComponentInParent<XROrigin>();
                    if (parentOrigin == null)
                    {
                        Debug.LogWarning($"Удаляем проблемный ARMeshManager: {manager.gameObject.name}");
                        GameObject.DestroyImmediate(manager.gameObject);
                    }
                }
            }
        }
        
        // Запускаем основную функцию исправления
        FixARMeshManagerHierarchy();
        
        Debug.Log("=== ИСПРАВЛЕНИЕ ЗАВЕРШЕНО ===");
    }

    // Добавляем метод для быстрого решения ошибки "Hierarchy not allowed"
    [MenuItem("CONTEXT/ARMeshManager/Fix Hierarchy Issues")]
    static void FixMeshManagerContextMenu(MenuCommand command)
    {
        ARMeshManager meshManager = (ARMeshManager)command.context;
        if (meshManager == null) return;
        
        Debug.Log($"Исправляем ARMeshManager из контекстного меню: {meshManager.gameObject.name}");
        
        // Находим XROrigin
        XROrigin xrOrigin = GameObject.FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            // Создаем XROrigin если его нет
            GameObject xrOriginObj = new GameObject("XR Origin");
            xrOrigin = xrOriginObj.AddComponent<XROrigin>();
            
            // Настраиваем основные компоненты
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginObj.transform);
            
            GameObject arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(cameraOffset.transform);
            Camera camera = arCamera.AddComponent<Camera>();
            
            xrOrigin.Camera = camera;
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            
            Debug.Log("Создан новый XROrigin");
        }
        
        // Сохраняем имя объекта
        string originalName = meshManager.gameObject.name;
        
        // Перемещаем под XROrigin
        meshManager.transform.SetParent(xrOrigin.transform);
        meshManager.transform.localPosition = Vector3.zero;
        meshManager.transform.localRotation = Quaternion.identity;
        meshManager.transform.localScale = Vector3.one;
        
        // Восстанавливаем имя если оно изменилось
        if (meshManager.gameObject.name != originalName)
        {
            meshManager.gameObject.name = originalName;
        }
        
        Debug.Log($"ARMeshManager {originalName} успешно перемещен под XROrigin");
    }
} 