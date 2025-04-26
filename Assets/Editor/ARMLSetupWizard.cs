using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using System.IO;
using ML.DeepLab;
using System.Reflection;
using Unity.Barracuda;
using UnityEditor.SceneManagement;
using System;

/// <summary>
/// Editor wizard for setting up the AR+ML system
/// </summary>
public class ARMLSetupWizard : EditorWindow
{
    private enum WizardStep
    {
        Welcome,
        CheckComponents,
        ConfigureMLModel,
        SetupComplete
    }
    
    private WizardStep currentStep = WizardStep.Welcome;
    
    // Model reference
    private UnityEngine.Object onnxModelAsset;
    private string modelPath = "";
    
    // Component references
    private GameObject arSystem;
    private GameObject mlSystem;
    private SegmentationManager segmentationManager;
    private MaskProcessor maskProcessor;
    private MLConnector mlConnector;
    private ARCameraManager arCameraManager;
    
    // Configuration options
    private bool createDebugViewer = true;
    private int wallClassId = 9; // Default ADE20K wall class ID
    private float segmentationInterval = 0.5f;
    private bool temporalSmoothing = true;
    private float classificationThreshold = 0.5f; // Default classification threshold
    
    [SerializeField] private NNModel onnxModel;
    [SerializeField] private string modelAssetPath = "Assets/ML/Models/model.onnx";
    [SerializeField] private bool autoDetectModel = true;
    
    [MenuItem("AR/ML Setup Wizard")]
    public static void ShowWindow()
    {
        ARMLSetupWizard window = GetWindow<ARMLSetupWizard>("AR+ML Setup Wizard");
        window.minSize = new Vector2(450, 400);
        window.maxSize = new Vector2(600, 600);
    }
    
    private void OnEnable()
    {
        // Find existing components
        arSystem = GameObject.Find("AR System");
        
        if (arSystem != null)
        {
            Transform mlSystemTransform = arSystem.transform.Find("ML System");
            if (mlSystemTransform != null)
            {
                mlSystem = mlSystemTransform.gameObject;
                segmentationManager = mlSystem.GetComponentInChildren<SegmentationManager>();
                maskProcessor = mlSystem.GetComponentInChildren<MaskProcessor>();
                mlConnector = mlSystem.GetComponentInChildren<MLConnector>();
            }
        }
        
        arCameraManager = FindFirstObjectByType<ARCameraManager>();
        
        // Try to find ONNX model assets in the project
        string[] onnxFiles = Directory.GetFiles(Application.dataPath, "*.onnx", SearchOption.AllDirectories);
        if (onnxFiles.Length > 0)
        {
            string relativePath = onnxFiles[0].Replace(Application.dataPath, "Assets");
            onnxModelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            modelPath = relativePath;
        }
        
        // Try to find model.onnx automatically
        if (autoDetectModel && onnxModel == null)
        {
            FindModelOnnx();
        }
    }
    
    private void FindModelOnnx()
    {
        // Try to find model.onnx using AssetDatabase
        string[] guids = AssetDatabase.FindAssets("model t:NNModel");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("model") && path.EndsWith(".onnx") || path.EndsWith(".onnx.meta"))
            {
                onnxModel = AssetDatabase.LoadAssetAtPath<NNModel>(path);
                modelAssetPath = path;
                Debug.Log($"ARMLSetupWizard: Found model.onnx at {path}");
                return;
            }
        }
        
        // If we couldn't find it, log a warning
        Debug.LogWarning("ARMLSetupWizard: Could not find model.onnx in the project. Please assign it manually.");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Настройка ML для AR", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        GUILayout.Label("ML модель");
        
        // ONNX model field with help text
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("ONNX модель", "Должен быть файл model.onnx. Использование других моделей может вызвать проблемы."));
        onnxModel = (NNModel)EditorGUILayout.ObjectField(onnxModel, typeof(NNModel), false);
        EditorGUILayout.EndHorizontal();
        
        if (onnxModel != null && !onnxModel.name.Contains("model"))
        {
            EditorGUILayout.HelpBox("Предупреждение: рекомендуется использовать 'model.onnx' вместо '" + onnxModel.name + "'. Это может вызвать проблемы с распознаванием стен.", MessageType.Warning);
        }
        else if (onnxModel == null) 
        {
            EditorGUILayout.HelpBox("Для корректной работы AR ML необходимо указать файл model.onnx", MessageType.Info);
        }
        
        EditorGUILayout.Space();
        GUILayout.Label("Настройки сегментации", EditorStyles.boldLabel);
        
        wallClassId = EditorGUILayout.IntSlider("ID класса стен", wallClassId, 0, 20);
        segmentationInterval = EditorGUILayout.Slider("Интервал сегментации (сек)", segmentationInterval, 0.1f, 2.0f);
        temporalSmoothing = EditorGUILayout.Toggle("Временное сглаживание", temporalSmoothing);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Настроить ML компоненты"))
        {
            SetupMLComponents();
        }
    }
    
    private void DrawWelcomeStep()
    {
        EditorGUILayout.LabelField("Welcome to AR+ML Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "This wizard will help you set up the Machine Learning components " +
            "for wall segmentation in your AR project.\n\n" +
            "You will need:\n" +
            "- An AR scene with AR System already set up\n" +
            "- An ONNX DeepLab model for wall segmentation\n\n" +
            "The wizard will create and configure all necessary components.", 
            MessageType.Info);
        
        EditorGUILayout.Space(10);
        
        if (arSystem == null)
        {
            EditorGUILayout.HelpBox(
                "AR System not found in the scene! Please set up AR Foundation first " +
                "using AR > Setup AR Scene (Basic) before continuing.",
                MessageType.Error);
        }
    }
    
    private void DrawCheckComponentsStep()
    {
        EditorGUILayout.LabelField("Step 1: Check Required Components", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);
        
        bool missingComponents = false;
        
        // Check AR System
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("AR System:", GUILayout.Width(150));
        if (arSystem != null)
        {
            EditorGUILayout.LabelField("✓ Found", EditorStyles.boldLabel);
        }
        else
        {
            EditorGUILayout.LabelField("✗ Missing", EditorStyles.boldLabel);
            missingComponents = true;
        }
        EditorGUILayout.EndHorizontal();
        
        // Check AR Camera Manager
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("AR Camera Manager:", GUILayout.Width(150));
        if (arCameraManager != null)
        {
            EditorGUILayout.LabelField("✓ Found", EditorStyles.boldLabel);
        }
        else
        {
            EditorGUILayout.LabelField("✗ Missing", EditorStyles.boldLabel);
            missingComponents = true;
        }
        EditorGUILayout.EndHorizontal();
        
        // Check ML System
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("ML System:", GUILayout.Width(150));
        if (mlSystem != null)
        {
            EditorGUILayout.LabelField("✓ Found", EditorStyles.boldLabel);
        }
        else
        {
            EditorGUILayout.LabelField("○ Will be created", EditorStyles.boldLabel);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(10);
        
        if (missingComponents)
        {
            EditorGUILayout.HelpBox(
                "Some required components are missing! Please set up AR Foundation first " +
                "using AR > Setup AR Scene (Basic) before continuing.",
                MessageType.Error);
                
            if (GUILayout.Button("Fix: Create AR System"))
            {
                // Call the AR scene setup method
                System.Type arSceneSetupType = System.Type.GetType("ARSceneSetupBasic");
                if (arSceneSetupType != null)
                {
                    var setupMethod = arSceneSetupType.GetMethod("SetupARScene", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (setupMethod != null)
                    {
                        setupMethod.Invoke(null, null);
                        // Refresh references
                        OnEnable();
                    }
                }
            }
        }
    }
    
    private void DrawConfigureModelStep()
    {
        EditorGUILayout.LabelField("Step 2: Configure ML Model", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);
        
        // ONNX model selection
        EditorGUILayout.LabelField("ONNX Model Asset:", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        onnxModelAsset = EditorGUILayout.ObjectField(onnxModelAsset, typeof(UnityEngine.Object), false);
        if (GUILayout.Button("Find ONNX Models", GUILayout.Width(150)))
        {
            string selectedPath = EditorUtility.OpenFilePanel("Select ONNX Model", Application.dataPath, "onnx");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                string relativePath = selectedPath.Replace(Application.dataPath, "Assets");
                onnxModelAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
                modelPath = relativePath;
            }
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(10);
        
        // Segmentation settings
        EditorGUILayout.LabelField("Segmentation Settings:", EditorStyles.boldLabel);
        wallClassId = EditorGUILayout.IntField("Wall Class ID:", wallClassId);
        segmentationInterval = EditorGUILayout.Slider("Segmentation Interval (s):", segmentationInterval, 0.1f, 2.0f);
        temporalSmoothing = EditorGUILayout.Toggle("Use Temporal Smoothing:", temporalSmoothing);
        
        EditorGUILayout.Space(10);
        
        // Debug options
        EditorGUILayout.LabelField("Debug Options:", EditorStyles.boldLabel);
        createDebugViewer = EditorGUILayout.Toggle("Create Debug Viewer:", createDebugViewer);
        
        if (onnxModelAsset == null)
        {
            EditorGUILayout.HelpBox(
                "An ONNX model is required for wall segmentation. Please select or find an ONNX model.",
                MessageType.Warning);
        }
    }
    
    private void DrawSetupCompleteStep()
    {
        EditorGUILayout.LabelField("Setup Complete", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);
        
        EditorGUILayout.HelpBox(
            "The ML System has been successfully set up in your AR scene.\n\n" +
            "Components created:\n" +
            "- SegmentationManager\n" +
            "- MaskProcessor\n" +
            "- MLConnector\n" +
            (createDebugViewer ? "- Debug viewer for mask visualization\n" : "") +
            "\nYou can now use the ML System to detect and paint walls in your AR application.",
            MessageType.Info);
            
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Select ML System in Hierarchy"))
        {
            if (mlSystem != null)
            {
                Selection.activeGameObject = mlSystem;
            }
        }
    }
    
    private void DrawNavigationButtons()
    {
        EditorGUILayout.BeginHorizontal();
        
        if (currentStep > WizardStep.Welcome)
        {
            if (GUILayout.Button("Previous", GUILayout.Width(100)))
            {
                currentStep--;
            }
        }
        
        GUILayout.FlexibleSpace();
        
        if (currentStep < WizardStep.SetupComplete)
        {
            string nextLabel = "Next";
            if (currentStep == WizardStep.ConfigureMLModel)
                nextLabel = "Create ML System";
                
            bool canContinue = true;
            
            // Check if we can continue
            if (currentStep == WizardStep.CheckComponents && arSystem == null)
                canContinue = false;
                
            if (currentStep == WizardStep.ConfigureMLModel && onnxModelAsset == null)
                canContinue = false;
                
            EditorGUI.BeginDisabledGroup(!canContinue);
            if (GUILayout.Button(nextLabel, GUILayout.Width(150)))
            {
                if (currentStep == WizardStep.ConfigureMLModel)
                {
                    // Create the ML System
                    CreateMLSystem();
                }
                
                currentStep++;
            }
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            if (GUILayout.Button("Close", GUILayout.Width(100)))
            {
                Close();
            }
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void CreateMLSystem()
    {
        if (arSystem == null)
        {
            Debug.LogError("AR System not found! Cannot create ML System.");
            return;
        }
        
        // Check if ML System already exists
        if (mlSystem == null)
        {
            // Create ML System
            mlSystem = new GameObject("ML System");
            mlSystem.transform.SetParent(arSystem.transform, false);
            
            // Create SegmentationManager
            GameObject segmentationManagerObj = new GameObject("SegmentationManager");
            segmentationManagerObj.transform.SetParent(mlSystem.transform, false);
            segmentationManager = segmentationManagerObj.AddComponent<SegmentationManager>();
            
            // Create MaskProcessor
            GameObject maskProcessorObj = new GameObject("MaskProcessor");
            maskProcessorObj.transform.SetParent(mlSystem.transform, false);
            maskProcessor = maskProcessorObj.AddComponent<MaskProcessor>();
            
            // Create MLConnector
            GameObject mlConnectorObj = new GameObject("MLConnector");
            mlConnectorObj.transform.SetParent(mlSystem.transform, false);
            mlConnector = mlConnectorObj.AddComponent<MLConnector>();
        }
        
        // Configure components
        if (segmentationManager != null)
        {
            // Set model asset using reflection or direct property access
            // Will depend on implementation of SegmentationManager
            
            // Try to find a setter for model asset
            var modelProperty = segmentationManager.GetType().GetProperty("ModelAsset");
            if (modelProperty != null && modelProperty.CanWrite)
            {
                modelProperty.SetValue(segmentationManager, onnxModelAsset);
            }
            
            // Set other properties
            var intervalProperty = segmentationManager.GetType().GetProperty("ProcessingInterval");
            if (intervalProperty != null && intervalProperty.CanWrite)
            {
                intervalProperty.SetValue(segmentationManager, segmentationInterval);
            }
        }
        
        // Configure connector
        if (mlConnector != null)
        {
            // Set references using SerializedObject
            SerializedObject serializedConnector = new SerializedObject(mlConnector);
            
            // Set ARCameraManager reference
            SerializedProperty cameraProp = serializedConnector.FindProperty("arCameraManager");
            if (cameraProp != null)
            {
                cameraProp.objectReferenceValue = arCameraManager;
            }
            
            // Set SegmentationManager reference
            SerializedProperty segmentationProp = serializedConnector.FindProperty("segmentationManager");
            if (segmentationProp != null)
            {
                segmentationProp.objectReferenceValue = segmentationManager;
            }
            
            // Set MaskProcessor reference
            SerializedProperty maskProcessorProp = serializedConnector.FindProperty("maskProcessor");
            if (maskProcessorProp != null)
            {
                maskProcessorProp.objectReferenceValue = maskProcessor;
            }
            
            // Apply changes
            serializedConnector.ApplyModifiedProperties();
        }
        
        // Create debug viewer if requested
        if (createDebugViewer)
        {
            // Find UI Canvas
            GameObject uiCanvas = GameObject.Find("UI Canvas");
            if (uiCanvas != null)
            {
                // Check if debug viewer already exists
                Transform existingViewer = uiCanvas.transform.Find("ML Debug Viewer");
                if (existingViewer == null)
                {
                    // Create ML Debug Viewer
                    GameObject mlDebugViewer = new GameObject("ML Debug Viewer");
                    mlDebugViewer.transform.SetParent(uiCanvas.transform, false);
                    
                    // Add RectTransform
                    RectTransform rectTransform = mlDebugViewer.AddComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(0.7f, 0.7f);
                    rectTransform.anchorMax = new Vector2(0.95f, 0.95f);
                    rectTransform.offsetMin = Vector2.zero;
                    rectTransform.offsetMax = Vector2.zero;
                    
                    // Add RawImage
                    UnityEngine.UI.RawImage rawImage = mlDebugViewer.AddComponent<UnityEngine.UI.RawImage>();
                    rawImage.color = Color.white;
                    
                    // Set the reference in MLConnector
                    if (mlConnector != null)
                    {
                        SerializedObject serializedConnector = new SerializedObject(mlConnector);
                        SerializedProperty debugImageProp = serializedConnector.FindProperty("debugRawImage");
                        if (debugImageProp != null)
                        {
                            debugImageProp.objectReferenceValue = rawImage;
                            serializedConnector.ApplyModifiedProperties();
                        }
                    }
                }
            }
        }
        
        // Mark scene as dirty
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
    
    private void SetupMLComponents()
    {
        try
        {
            // Ensure we have a model asset
            if (onnxModel == null)
            {
                FindModelOnnx();
                if (onnxModel == null)
                {
                    EditorUtility.DisplayDialog("Ошибка", "Не найден файл модели model.onnx. Пожалуйста, укажите его вручную.", "OK");
                    return;
                }
            }
            
            // Validate the model asset
            if (!onnxModel.name.Contains("model"))
            {
                bool proceed = EditorUtility.DisplayDialog("Предупреждение", 
                    $"Рекомендуется использовать файл 'model.onnx' вместо '{onnxModel.name}'. Это может вызвать проблемы с распознаванием стен. Хотите продолжить?", 
                    "Продолжить", "Отмена");
                
                if (!proceed)
                    return;
            }
            
            // Find or create necessary objects
            GameObject arSystem = FindOrCreateARSystem();
            if (arSystem == null)
            {
                EditorUtility.DisplayDialog("Ошибка", "Не удалось найти или создать AR System.", "OK");
                return;
            }
            
            // Create ML System
            GameObject mlSystem = FindOrCreateMLSystem(arSystem);
            
            // Create SegmentationManager
            SegmentationManager segmentationManager = SetupSegmentationManager(mlSystem);
            
            // Create Enhanced DeepLab Predictor or fallback to standard
            bool createdPredictor = SetupDeepLabPredictor(mlSystem, segmentationManager);
            
            if (segmentationManager != null)
            {
                // Reflection to set properties
                Type segManagerType = segmentationManager.GetType();
                
                // Find and set the modelAsset field
                FieldInfo modelField = segManagerType.GetField("modelAsset", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (modelField != null && onnxModel != null)
                {
                    modelField.SetValue(segmentationManager, onnxModel);
                    Debug.Log($"ML Setup: Set model asset '{onnxModel.name}' on SegmentationManager");
                }
                
                // Set wall class ID
                FieldInfo wallClassIdField = segManagerType.GetField("wallClassId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (wallClassIdField != null)
                {
                    wallClassIdField.SetValue(segmentationManager, (byte)wallClassId);
                    Debug.Log($"ML Setup: Set wall class ID to {wallClassId}");
                }
                
                // Set classification threshold - using a property if available
                PropertyInfo thresholdProperty = segManagerType.GetProperty("ClassificationThreshold");
                if (thresholdProperty != null)
                {
                    thresholdProperty.SetValue(segmentationManager, classificationThreshold);
                    Debug.Log($"ML Setup: Set classification threshold to {classificationThreshold}");
                }
                else
                {
                    // Try field
                    FieldInfo thresholdField = segManagerType.GetField("classificationThreshold", 
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (thresholdField != null)
                    {
                        thresholdField.SetValue(segmentationManager, classificationThreshold);
                        Debug.Log($"ML Setup: Set classification threshold to {classificationThreshold}");
                    }
                }
            }
            
            // Mark scene as dirty
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            
            EditorUtility.DisplayDialog("Успех", "ML компоненты настроены успешно!", "OK");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Ошибка при настройке ML компонентов: {ex.Message}");
            EditorUtility.DisplayDialog("Ошибка", $"Ошибка при настройке ML: {ex.Message}", "OK");
        }
    }
    
    private GameObject FindOrCreateARSystem()
    {
        // Find existing AR System
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem != null)
            return arSystem;
        
        // Create new AR System
        arSystem = new GameObject("AR System");
        Debug.Log("Created new AR System");
        
        return arSystem;
    }
    
    private GameObject FindOrCreateMLSystem(GameObject arSystem)
    {
        // Check if ML System already exists
        Transform existingMLSystem = arSystem.transform.Find("ML System");
        if (existingMLSystem != null)
            return existingMLSystem.gameObject;
        
        // Create ML System parent GameObject
        GameObject mlSystem = new GameObject("ML System");
        mlSystem.transform.SetParent(arSystem.transform, false);
        Debug.Log("Created new ML System");
        
        return mlSystem;
    }
    
    private SegmentationManager SetupSegmentationManager(GameObject mlSystem)
    {
        // Check if SegmentationManager already exists in ML System
        segmentationManager = FindFirstObjectByType<SegmentationManager>();
        if (segmentationManager != null)
        {
            // Move it to ML System if it's not already there
            if (segmentationManager.transform.parent != mlSystem.transform)
            {
                segmentationManager.transform.SetParent(mlSystem.transform, false);
            }
            return segmentationManager;
        }
        
        // Check if SegmentationManager exists elsewhere in the scene
        segmentationManager = FindFirstObjectByType<SegmentationManager>();
        if (segmentationManager != null)
        {
            // Move it to ML System if it's not already there
            if (segmentationManager.transform.parent != mlSystem.transform)
            {
                segmentationManager.transform.SetParent(mlSystem.transform, false);
                Debug.Log("Moved existing SegmentationManager to ML System");
            }
            return segmentationManager;
        }
        
        // Create new SegmentationManager
        GameObject segManagerObj = new GameObject("SegmentationManager");
        segManagerObj.transform.SetParent(mlSystem.transform, false);
        segmentationManager = segManagerObj.AddComponent<SegmentationManager>();
        Debug.Log("Created new SegmentationManager");
        
        return segmentationManager;
    }
    
    private bool SetupDeepLabPredictor(GameObject mlSystem, SegmentationManager segmentationManager)
    {
        // Try to find EnhancedDeepLabPredictor
        EnhancedDeepLabPredictor enhancedPredictor = null;
        
        // First check if it exists in ML System
        enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
        
        // If not found in ML System, check the entire scene
        if (enhancedPredictor == null)
        {
            enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
            if (enhancedPredictor != null && enhancedPredictor.transform.parent != mlSystem.transform)
            {
                // Move it to ML System
                enhancedPredictor.transform.SetParent(mlSystem.transform, false);
                Debug.Log("Moved existing EnhancedDeepLabPredictor to ML System");
            }
        }
        
        // If we still don't have an EnhancedDeepLabPredictor, try to create one
        if (enhancedPredictor == null)
        {
            try
            {
                GameObject predictorObj = new GameObject("EnhancedDeepLabPredictor");
                predictorObj.transform.SetParent(mlSystem.transform, false);
                enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
                Debug.Log("Created new EnhancedDeepLabPredictor");
                
                // Set model asset
                if (onnxModel != null)
                {
                    // Use reflection to access the modelAsset field
                    FieldInfo modelField = enhancedPredictor.GetType().GetField("modelAsset", 
                        BindingFlags.Instance | BindingFlags.Public);
                    if (modelField != null)
                    {
                        modelField.SetValue(enhancedPredictor, onnxModel);
                        Debug.Log($"Set model asset '{onnxModel.name}' on EnhancedDeepLabPredictor");
                    }
                }
                
                // Set wall class ID
                enhancedPredictor.WallClassId = wallClassId;
                Debug.Log($"Set wall class ID to {wallClassId} on EnhancedDeepLabPredictor");
                
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to create EnhancedDeepLabPredictor: {ex.Message}");
                
                // Try to create a standard DeepLabPredictor as fallback
                try
                {
                    GameObject predictorObj = new GameObject("DeepLabPredictor");
                    predictorObj.transform.SetParent(mlSystem.transform, false);
                    DeepLabPredictor predictor = predictorObj.AddComponent<DeepLabPredictor>();
                    
                    // Set model asset
                    if (onnxModel != null)
                    {
                        // Use reflection to access the modelAsset field
                        FieldInfo modelField = predictor.GetType().GetField("modelAsset", 
                            BindingFlags.Instance | BindingFlags.Public);
                        if (modelField != null)
                        {
                            modelField.SetValue(predictor, onnxModel);
                            Debug.Log($"Set model asset '{onnxModel.name}' on DeepLabPredictor");
                        }
                    }
                    
                    // Set wall class ID
                    predictor.WallClassId = wallClassId;
                    Debug.Log($"Set wall class ID to {wallClassId} on DeepLabPredictor");
                    
                    return true;
                }
                catch (System.Exception innerEx)
                {
                    Debug.LogError($"Failed to create DeepLabPredictor: {innerEx.Message}");
                    return false;
                }
            }
        }
        else
        {
            // Update existing EnhancedDeepLabPredictor
            if (onnxModel != null)
            {
                // Use reflection to access the modelAsset field
                FieldInfo modelField = enhancedPredictor.GetType().GetField("modelAsset", 
                    BindingFlags.Instance | BindingFlags.Public);
                if (modelField != null)
                {
                    // Check if model is already assigned
                    NNModel currentModel = modelField.GetValue(enhancedPredictor) as NNModel;
                    if (currentModel == null)
                    {
                        modelField.SetValue(enhancedPredictor, onnxModel);
                        Debug.Log($"Set model asset '{onnxModel.name}' on existing EnhancedDeepLabPredictor");
                    }
                }
            }
            
            // Update wall class ID
            enhancedPredictor.WallClassId = wallClassId;
            Debug.Log($"Updated wall class ID to {wallClassId} on existing EnhancedDeepLabPredictor");
            
            return true;
        }
    }
} 