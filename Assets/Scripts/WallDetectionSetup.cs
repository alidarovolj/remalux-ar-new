using UnityEngine;
using System.Collections;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor

/// <summary>
/// Скрипт для инициализации улучшенного распознавания стен.
/// Добавьте этот компонент в сцену для автоматического добавления всех
/// необходимых компонентов и улучшения распознавания стен.
/// </summary>
public class WallDetectionSetup : MonoBehaviour
{
    [Header("Setup Settings")]
    public bool autoSetup = true;
    public bool useEnhancedPredictor = true;
    public bool createUIPanel = true;
    public bool applyFixesToOriginal = true;

    [Header("Detection Settings")]
    public float initialThreshold = 0.15f; // Снижаем порог для лучшего обнаружения
    public byte wallClassId = 9; // Default wall class ID
    public bool useArgMaxMode = true;

    private EnhancedDeepLabPredictor enhancedPredictor;
    private DeepLabPredictor basicPredictor;
    private WallDetectionOptimizer optimizer;
    private WallDetectionUI uiManager;

    private void Start()
    {
        // Check if auto setup is enabled
        if (autoSetup)
        {
            StartCoroutine(SetupSystem());
        }
        
        // Set up the enhanced predictor and integrations
        if (useEnhancedPredictor)
        {
            SetupEnhancedPredictor();
        }
    }

    private IEnumerator SetupSystem()
    {
        Debug.Log("WallDetectionSetup: Starting system setup...");

        // Создаем DeepLabFixer для базового предиктора
        yield return StartCoroutine(CreateDeepLabFixer());

        // Создаем или находим EnhancedPredictor
        yield return StartCoroutine(CreateEnhancedPredictor());

        // Создаем оптимизатор
        optimizer = CreateOptimizer();
        if (optimizer != null)
        {
            Debug.Log("WallDetectionSetup: Created WallDetectionOptimizer");
        }

        // Создаем UI если нужно
        if (createUIPanel)
        {
            uiManager = CreateUIManager();
            if (uiManager != null)
            {
                Debug.Log("WallDetectionSetup: Created WallDetectionUI");
            }
        }

        // Применяем настройки
        ApplySettings();

        Debug.Log("WallDetectionSetup: System setup complete!");
    }

    private void ApplySettings()
    {
        if (optimizer != null)
        {
            optimizer.classificationThreshold = initialThreshold;
            optimizer.wallClassId = wallClassId;
            optimizer.useArgMaxMode = useArgMaxMode;
            optimizer.ApplyOptimizerSettings();
        }

        if (enhancedPredictor != null)
        {
            enhancedPredictor.WallClassId = wallClassId;
            enhancedPredictor.ClassificationThreshold = initialThreshold;
            enhancedPredictor.useArgMaxMode = useArgMaxMode;
        }

        Debug.Log($"WallDetectionSetup: Applied settings - Threshold: {initialThreshold}, Wall Class: {wallClassId}, ArgMax: {useArgMaxMode}");
    }

    private IEnumerator CreateDeepLabFixer()
    {
        // Проверяем, существует ли уже DeepLabFixer
        DeepLabFixer fixer = FindFirstObjectByType<DeepLabFixer>();
        
        if (fixer == null)
        {
            GameObject fixerObj = new GameObject("DeepLabFixer");
            fixerObj.transform.SetParent(transform);
            fixer = fixerObj.AddComponent<DeepLabFixer>();
            Debug.Log("WallDetectionSetup: Created DeepLabFixer");
        }
        else
        {
            Debug.Log("WallDetectionSetup: Using existing DeepLabFixer");
        }
        
        // Устанавливаем параметры
        fixer.wallThreshold = initialThreshold;
        yield break;
    }
    
    private IEnumerator CreateEnhancedPredictor()
    {
        yield return new WaitForSeconds(0.2f);

        // First check if there's already an EnhancedDeepLabPredictor in the scene
        EnhancedDeepLabPredictor existingEnhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
        if (existingEnhancedPredictor != null)
        {
            Debug.Log("WallDetectionSetup: Using existing EnhancedDeepLabPredictor created by another component");
            enhancedPredictor = existingEnhancedPredictor;
            yield break;
        }

        DeepLabPredictor originalPredictor = FindFirstObjectByType<DeepLabPredictor>();
        if (originalPredictor != null)
        {
            // Wait a bit longer to ensure original predictor is fully initialized
            yield return new WaitForSeconds(1.0f);
            
            // Check if the original predictor has a model asset
            if (originalPredictor.modelAsset == null)
            {
                Debug.LogWarning("WallDetectionSetup: Original predictor's model asset is null! Looking for alternatives...");
                
                // Try to find model asset in resources folder
                var modelAssets = Resources.FindObjectsOfTypeAll<Unity.Barracuda.NNModel>();
                if (modelAssets != null && modelAssets.Length > 0)
                {
                    foreach (var model in modelAssets)
                    {
                        if (model.name.Contains("DeepLab") || model.name.Contains("Segmentation"))
                        {
                            Debug.Log($"WallDetectionSetup: Found potential model asset: {model.name}");
                            // Assign to the original predictor
                            originalPredictor.modelAsset = model;
                            break;
                        }
                    }
                }
                
                // Final check if we found a model
                if (originalPredictor.modelAsset == null)
                {
                    Debug.LogError("WallDetectionSetup: Could not find any model asset. Skipping enhanced predictor creation.");
                    yield break;
                }
            }
            
            // Store reference to model asset
            var modelAsset = originalPredictor.modelAsset;
            Debug.Log($"WallDetectionSetup: Using model asset '{modelAsset.name}' from original predictor");
            
            // Create a new enhanced predictor
            GameObject enhancedPredictorObj = new GameObject("EnhancedDeepLabPredictor");
            enhancedPredictor = enhancedPredictorObj.AddComponent<EnhancedDeepLabPredictor>();
            
            // Directly assign the model asset first
            enhancedPredictor.modelAsset = modelAsset;
            
            // Try to initialize the enhanced predictor from source
            try
            {
                enhancedPredictor.InitializeFromSource(originalPredictor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WallDetectionSetup: Error during initialization from source: {e.Message}");
                // Continue anyway as we've already assigned the model asset
            }
            
            // Double-check that model asset was correctly assigned
            if (enhancedPredictor.modelAsset == null)
            {
                Debug.LogWarning("WallDetectionSetup: Model asset not copied correctly. Assigning directly.");
                enhancedPredictor.modelAsset = modelAsset;
            }
            
            // Log model asset information
            Debug.Log($"WallDetectionSetup: Enhanced predictor using model: {enhancedPredictor.modelAsset?.name ?? "NULL"}");
            
            // Set enhanced settings
            enhancedPredictor.useArgMaxMode = useArgMaxMode;
            enhancedPredictor.applyNoiseReduction = true;
            enhancedPredictor.applyTemporalSmoothing = true;
            enhancedPredictor.applyWallFilling = true;
            
            // Connect event listeners to transfer events from original to enhanced
            TryConnectEventListeners(enhancedPredictor, originalPredictor);
            
            // Disable the original predictor if we're not in a coordinated setup with FixARMLController
            FixARMLController fixController = FindFirstObjectByType<FixARMLController>();
            if (fixController == null || !fixController.enabled)
            {
                originalPredictor.enabled = false;
                Debug.Log("WallDetectionSetup: Disabled original predictor");
            }
            else
            {
                Debug.Log("WallDetectionSetup: Found active FixARMLController, leaving original predictor management to it");
            }
            
            Debug.Log("WallDetectionSetup: Created EnhancedDeepLabPredictor successfully");
        }
        else
        {
            Debug.LogError("WallDetectionSetup: Could not find original DeepLabPredictor");
        }
    }
    
    private void TryConnectEventListeners(EnhancedDeepLabPredictor newPredictor, DeepLabPredictor originalPredictor)
    {
        try
        {
            // С помощью reflection находим все обработчики событий оригинального предиктора
            var predictorType = originalPredictor.GetType();
            var eventField = predictorType.GetField("OnSegmentationResult", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                
            if (eventField != null)
            {
                var eventDelegate = eventField.GetValue(originalPredictor);
                if (eventDelegate != null)
                {
                    // Копируем обработчики в новый предиктор
                    var listeners = eventDelegate as System.Delegate;
                    if (listeners != null)
                    {
                        foreach (var listener in listeners.GetInvocationList())
                        {
                            // Добавляем каждый обработчик к новому событию
                            newPredictor.OnSegmentationResult += (EnhancedDeepLabPredictor.SegmentationResultHandler)listener;
                        }
                        
                        Debug.Log("WallDetectionSetup: Successfully copied event listeners from original predictor");
                    }
                }
            }
            else
            {
                // Ищем все компоненты, которые могут быть слушателями
                // Например, ARMLController и WallColorizer
                var armlController = FindFirstObjectByType<ARMLController>();
                if (armlController != null)
                {
                    // Проверяем, есть ли у него метод для приема маски
                    var handleMethod = armlController.GetType().GetMethod("HandleSegmentationResult", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        
                    if (handleMethod != null)
                    {
                        // Создаем делегат и подписываемся
                        newPredictor.OnSegmentationResult += (mask) => {
                            handleMethod.Invoke(armlController, new object[] { mask });
                        };
                        
                        Debug.Log("WallDetectionSetup: Connected EnhancedPredictor to ARMLController");
                    }
                }
                
                // Аналогично для WallColorizer
                var wallColorizer = FindFirstObjectByType<WallColorizer>();
                if (wallColorizer != null)
                {
                    var updateMethod = wallColorizer.GetType().GetMethod("UpdateWallMask", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        
                    if (updateMethod != null)
                    {
                        newPredictor.OnSegmentationResult += (mask) => {
                            updateMethod.Invoke(wallColorizer, new object[] { mask });
                        };
                        
                        Debug.Log("WallDetectionSetup: Connected EnhancedPredictor to WallColorizer");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"WallDetectionSetup: Error connecting event listeners: {e.Message}");
        }
    }
    
    private WallDetectionOptimizer CreateOptimizer()
    {
        // Проверяем, существует ли уже WallDetectionOptimizer
        WallDetectionOptimizer optimizer = FindFirstObjectByType<WallDetectionOptimizer>();
        
        if (optimizer == null)
        {
            GameObject optimizerObj = new GameObject("WallDetectionOptimizer");
            optimizerObj.transform.SetParent(transform);
            optimizer = optimizerObj.AddComponent<WallDetectionOptimizer>();
            Debug.Log("WallDetectionSetup: Created WallDetectionOptimizer");
        }
        else
        {
            Debug.Log("WallDetectionSetup: Using existing WallDetectionOptimizer");
        }
        
        // Устанавливаем ссылки на компоненты
        optimizer.fixer = FindFirstObjectByType<DeepLabFixer>();
        optimizer.enhancedPredictor = enhancedPredictor;
        optimizer.useEnhancedPredictor = useEnhancedPredictor;
        optimizer.initialThreshold = initialThreshold;
        return optimizer;
    }
    
    private WallDetectionUI CreateUIManager()
    {
        // Проверяем, существует ли уже WallDetectionUI
        WallDetectionUI uiManager = FindFirstObjectByType<WallDetectionUI>();
        
        if (uiManager == null)
        {
            GameObject uiObj = new GameObject("WallDetectionUI");
            uiObj.transform.SetParent(transform);
            uiManager = uiObj.AddComponent<WallDetectionUI>();
            Debug.Log("WallDetectionSetup: Created WallDetectionUI");
        }
        else
        {
            Debug.Log("WallDetectionSetup: Using existing WallDetectionUI");
        }
        
        // Устанавливаем ссылки на компоненты
        uiManager.optimizer = optimizer;
        return uiManager;
    }
    
    // Публичный метод для ручного запуска настройки
    public void SetupWallDetection()
    {
        StopAllCoroutines();
        StartCoroutine(SetupSystem());
    }

    private void SetupEnhancedPredictor()
    {
        try
        {
            // Find the AR system
            GameObject arSystem = GameObject.Find("AR System");
            if (arSystem == null)
            {
                Debug.LogError("WallDetectionSetup: AR System not found");
                return;
            }
            
            // Find the ML system
            GameObject mlSystem = GameObject.Find("ML System");
            if (mlSystem == null)
            {
                Debug.LogError("WallDetectionSetup: ML System not found");
                return;
            }
            
            // First check if the enhanced predictor already exists
            enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
            
            if (enhancedPredictor == null)
            {
                Debug.Log("WallDetectionSetup: Creating new EnhancedDeepLabPredictor");
                
                // Create the enhanced predictor
                GameObject predictorObj = new GameObject("Enhanced DeepLab Predictor");
                predictorObj.transform.SetParent(mlSystem.transform);
                enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
                
                // Find the original predictor
                DeepLabPredictor originalPredictor = FindFirstObjectByType<DeepLabPredictor>();
                if (originalPredictor != null)
                {
                    // Copy settings from the original predictor
                    enhancedPredictor.modelAsset = originalPredictor.modelAsset;
                    enhancedPredictor.WallClassId = 9; // Always use ADE20K wall class ID (9)
                    enhancedPredictor.inputWidth = originalPredictor.inputWidth > 0 ? originalPredictor.inputWidth : 512;
                    enhancedPredictor.inputHeight = originalPredictor.inputHeight > 0 ? originalPredictor.inputHeight : 512;
                    enhancedPredictor.ClassificationThreshold = initialThreshold;
                    enhancedPredictor.useArgMaxMode = useArgMaxMode;
                    
                    // Enable post-processing
                    enhancedPredictor.applyNoiseReduction = true;
                    enhancedPredictor.applyWallFilling = true;
                    enhancedPredictor.applyTemporalSmoothing = true;
                    enhancedPredictor.debugMode = true;
                    
                    Debug.Log("WallDetectionSetup: Configured EnhancedDeepLabPredictor with settings from original predictor");
                }
                else
                {
                    // Set default values
                    enhancedPredictor.WallClassId = 9; // ADE20K wall class ID (9)
                    enhancedPredictor.inputWidth = 512;
                    enhancedPredictor.inputHeight = 512;
                    enhancedPredictor.ClassificationThreshold = initialThreshold;
                    enhancedPredictor.useArgMaxMode = true;
                    enhancedPredictor.applyNoiseReduction = true;
                    enhancedPredictor.applyWallFilling = true;
                    enhancedPredictor.applyTemporalSmoothing = true;
                    enhancedPredictor.debugMode = true;
                    
                    Debug.Log("WallDetectionSetup: Configured EnhancedDeepLabPredictor with default settings");
                }
            }
            else
            {
                // Update existing predictor settings
                enhancedPredictor.WallClassId = 9; // ADE20K wall class ID (9)
                enhancedPredictor.ClassificationThreshold = initialThreshold;
                enhancedPredictor.useArgMaxMode = useArgMaxMode;
                enhancedPredictor.applyNoiseReduction = true;
                enhancedPredictor.applyWallFilling = true;
                
                Debug.Log("WallDetectionSetup: Updated existing EnhancedDeepLabPredictor");
            }
            
            // Setup ARWallPainter component for coordinating wall detection
            ARPlaneManager planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (planeManager != null)
            {
                // Check if ARWallPainter already exists
                ARWallPainter wallPainter = FindFirstObjectByType<ARWallPainter>();
                
                if (wallPainter == null)
                {
                    // Create the wall painter component
                    wallPainter = planeManager.gameObject.AddComponent<ARWallPainter>();
                    
                    // Set up references
                    wallPainter.enabled = false; // Disable until configuration is complete
                    
                    // Find camera manager
                    ARCameraManager cameraManager = FindFirstObjectByType<ARCameraManager>();
                    if (cameraManager != null)
                        wallPainter._cameraManager = cameraManager;
                    
                    // Reference the enhanced predictor
                    wallPainter._predictor = enhancedPredictor;
                    
                    // Load wall material
                    Material wallMat = Resources.Load<Material>("Materials/WallMaterial");
                    if (wallMat == null)
                    {
                        // Try other common locations
                        wallMat = Resources.Load<Material>("WallMaterial");
                    }
                    
                    if (wallMat == null)
                    {
                        // Create a default material if none exists
                        wallMat = new Material(Shader.Find("Standard"));
                        wallMat.color = new Color(0.2f, 0.6f, 1.0f, 0.5f);
                        Debug.LogWarning("WallDetectionSetup: Wall material not found, using a generated default material");
                    }
                    
                    wallPainter._wallMaterial = wallMat;
                    
                    // Configure settings
                    wallPainter._wallClassId = 9;
                    wallPainter._wallConfidenceThreshold = initialThreshold;
                    wallPainter.enabled = true;
                    
                    Debug.Log("WallDetectionSetup: Created and configured ARWallPainter");
                }
                else
                {
                    // Update existing wall painter settings
                    wallPainter._predictor = enhancedPredictor;
                    wallPainter._wallClassId = 9;
                    wallPainter._wallConfidenceThreshold = initialThreshold;
                    
                    Debug.Log("WallDetectionSetup: Updated existing ARWallPainter");
                }
            }
            else
            {
                Debug.LogWarning("WallDetectionSetup: ARPlaneManager not found, skipping ARWallPainter setup");
            }
            
            // Ensure that the WallMeshRenderer has the correct settings
            WallMeshRenderer wallMeshRenderer = FindFirstObjectByType<WallMeshRenderer>();
            if (wallMeshRenderer != null)
            {
                wallMeshRenderer.Predictor = enhancedPredictor;
                wallMeshRenderer._wallClassId = 9; // ADE20K wall class ID (9)
                wallMeshRenderer.WallConfidenceThreshold = initialThreshold;
                wallMeshRenderer._onlyUseVerticalPlanes = true;
                
                Debug.Log("WallDetectionSetup: Updated WallMeshRenderer settings");
            }
            
            // Connect events and listeners
            try
            {
                // Find WallColorizer
                WallColorizer wallColorizer = FindFirstObjectByType<WallColorizer>();
                if (wallColorizer != null)
                {
                    // Connect enhanced predictor to wall colorizer
                    var updateMethod = wallColorizer.GetType().GetMethod("UpdateWallMask", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        
                    if (updateMethod != null)
                    {
                        enhancedPredictor.OnSegmentationResult += (mask) => {
                            updateMethod.Invoke(wallColorizer, new object[] { mask });
                        };
                        
                        Debug.Log("WallDetectionSetup: Connected EnhancedPredictor to WallColorizer");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WallDetectionSetup: Error connecting event listeners: {e.Message}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallDetectionSetup: Error in SetupEnhancedPredictor: {e.Message}\n{e.StackTrace}");
        }
    }
} 