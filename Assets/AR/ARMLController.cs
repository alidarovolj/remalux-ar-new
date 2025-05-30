using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// Адаптер для управления ML-системой
public class ARMLManagerAdapter : MonoBehaviour 
{
    public SegmentationManager segmentationManager;
    
    // Метод для обработки текущего кадра
    public bool CaptureCurrentFrame()
    {
        if (segmentationManager == null)
            return false;
            
        // Заглушка для совместимости - фактическая обработка происходит через ARCameraManager.frameReceived
        return true;
    }
}

/// <summary>
/// Controls the ML processing
/// </summary>
public class ARMLController : MonoBehaviour
{
    /// <summary>
    /// The interval between ML captures
    /// </summary>
    public float predictionInterval = 1f;

    /// <summary>
    /// Whether to run the ML processing
    /// </summary>
    public bool isRunning = true;

    /// <summary>
    /// Whether to enable debug mode
    /// </summary>
    public bool debugMode = false;

    /// <summary>
    /// Whether to use the enhanced predictor
    /// </summary>
    public bool useEnhancedPredictor = true;

    /// <summary>
    /// The last time a frame was captured
    /// </summary>
    private float lastCaptureTime = 0f;

    /// <summary>
    /// The ML manager
    /// </summary>
    [SerializeField] private ARMLManagerAdapter mlManager;

    /// <summary>
    /// The camera manager
    /// </summary>
    [SerializeField] private ARCameraManager cameraManager;

    /// <summary>
    /// Reference to the AR session
    /// </summary>
    [SerializeField] private ARSession arSession;

    /// <summary>
    /// Reference to AR Plane Manager for plane detection
    /// </summary>
    [SerializeField] private ARPlaneManager arPlaneManager;

    /// <summary>
    /// The enhanced predictor
    /// </summary>
    [SerializeField] private EnhancedDeepLabPredictor enhancedPredictor;
    
    /// <summary>
    /// The segmentation manager
    /// </summary>
    [SerializeField] private SegmentationManager segmentationManager;
    
    /// <summary>
    /// The mask processor
    /// </summary>
    [SerializeField] private MaskProcessor maskProcessor;
    
    /// <summary>
    /// Indicates if AR has been started
    /// </summary>
    private bool arStarted = false;
    
    /// <summary>
    /// Maximum time to wait for AR session to be ready (in seconds)
    /// </summary>
    public float maxWaitTimeForSession = 10f;
    
    /// <summary>
    /// Flag to indicate if we're waiting for the session to be ready
    /// </summary>
    private bool waitingForSession = false;

    private void Awake()
    {
        // Try to find all required references at startup
        FindMissingReferences();
        
        // Подписываемся на событие получения кадра, если есть ARCameraManager
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnCameraFrameReceived;
            if (debugMode)
                Debug.Log("ARMLController: Subscribed to camera frame events");
        }
    }
    
    private void OnDisable()
    {
        // Отписываемся от события при выключении компонента
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }

    private void Start()
    {
        // Validate references when component starts
        ValidateReferences();
    }

    private void Update()
    {
        // Update ML processing if enabled and we're not using the direct frame subscription
        if (isRunning && cameraManager == null)
        {
            UpdateML();
        }
    }
    
    // Обработчик события получения кадра от ARCameraManager
    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (!isRunning) return;
        
        // Проверяем интервал между обработками для производительности
        if (Time.time - lastCaptureTime < predictionInterval) return;
        
        // Пытаемся получить последний CPU-кадр
        if (cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            try
            {
                // Создаем текстуру для обработки кадра
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(image.width, image.height),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };
                
                // Создаем текстуру для обработки
                var texture = new Texture2D(
                    conversionParams.outputDimensions.x,
                    conversionParams.outputDimensions.y,
                    conversionParams.outputFormat,
                    false);
                
                // Выделяем буфер для конвертации
                var rawTextureData = texture.GetRawTextureData<byte>();
                
                // Преобразование с использованием альтернативного подхода
                unsafe 
                {
                    image.Convert(conversionParams, 
                        new System.IntPtr(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(rawTextureData)),
                        rawTextureData.Length);
                }
                
                // Применяем изменения к текстуре
                texture.Apply();
                
                // Отправляем текстуру на обработку в SegmentationManager
                if (segmentationManager != null)
                {
                    // Pass texture dimensions as targetResolution
                    Vector2Int targetResolution = new Vector2Int(texture.width, texture.height);
                    segmentationManager.ProcessCameraFrame(texture, targetResolution);
                    lastCaptureTime = Time.time;
                    
                    if (debugMode && Time.frameCount % 100 == 0)
                        Debug.Log("ARMLController: Frame processed via direct camera subscription");
                }
                
                // Удаляем временную текстуру
                Destroy(texture);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ARMLController: Error processing camera frame: {e.Message}");
            }
            finally
            {
                // Обязательно освобождаем ресурсы
                image.Dispose();
            }
        }
    }

    /// <summary>
    /// Finds any missing references automatically
    /// </summary>
    private void FindMissingReferences()
    {
        // Find MLManager
        if (mlManager == null)
        {
            mlManager = FindObjectOfType<ARMLManagerAdapter>();
            if (mlManager == null)
            {
                // Если MLManager не найден, создаем его
                GameObject mlManagerObj = new GameObject("AR ML Manager Adapter");
                mlManager = mlManagerObj.AddComponent<ARMLManagerAdapter>();
                if (debugMode)
                    Debug.Log("ARMLController: Created ARMLManagerAdapter component");
            }
            else if (debugMode)
            {
                Debug.Log("ARMLController: Found ARMLManagerAdapter automatically");
            }
        }

        // Find ARCameraManager
        if (cameraManager == null)
        {
            // First try to find it through XROrigin's camera
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
                if (cameraManager != null && debugMode)
                    Debug.Log("ARMLController: Found ARCameraManager on XROrigin camera");
            }
            
            // Fallback to generic search
            if (cameraManager == null)
            {
                cameraManager = FindObjectOfType<ARCameraManager>();
                if (cameraManager != null && debugMode)
                    Debug.Log("ARMLController: Found ARCameraManager in scene");
            }
        }
        
        // Find ARSession
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
            if (arSession != null && debugMode)
                Debug.Log("ARMLController: Found ARSession in scene");
        }
        
        // Find ARPlaneManager
        if (arPlaneManager == null)
        {
            arPlaneManager = FindObjectOfType<ARPlaneManager>();
            if (arPlaneManager != null && debugMode)
                Debug.Log("ARMLController: Found ARPlaneManager in scene");
        }
        
        // Find SegmentationManager
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager != null)
            {
                // Устанавливаем связь с MLManager
                if (mlManager != null && mlManager.segmentationManager == null)
                {
                    mlManager.segmentationManager = segmentationManager;
                }
                
                if (debugMode)
                    Debug.Log("ARMLController: Found SegmentationManager in scene");
            }
        }
        else if (mlManager != null && mlManager.segmentationManager == null)
        {
            // Устанавливаем связь с MLManager, если она еще не установлена
            mlManager.segmentationManager = segmentationManager;
        }
        
        // Find MaskProcessor
        if (maskProcessor == null)
        {
            maskProcessor = FindObjectOfType<MaskProcessor>();
            if (maskProcessor != null && debugMode)
                Debug.Log("ARMLController: Found MaskProcessor in scene");
        }
        
        // Find EnhancedDeepLabPredictor if we're using it
        if (useEnhancedPredictor && enhancedPredictor == null)
        {
            enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            if (enhancedPredictor != null && debugMode)
                Debug.Log("ARMLController: Found EnhancedDeepLabPredictor in scene");
        }
    }

    /// <summary>
    /// Updates the ML processing
    /// </summary>
    private void UpdateML()
    {
        if (!isRunning)
            return;

        // Make sure we have valid components
        if (!ValidateReferences())
        {
            if (Time.frameCount % 300 == 0) // Only log occasionally to avoid spam
            {
                Debug.LogWarning("ARMLController: Missing required references for ML processing");
            }
            return;
        }

        // Check if we should capture a new frame
        if (Time.time - lastCaptureTime >= predictionInterval)
        {
            try
            {
                // Request ML manager to capture and process a frame
                if (mlManager != null)
                {
                    // Use CaptureCurrentFrame and handle frame directly 
                    // since CaptureAndProcessFrame might be missing
                    bool success = ProcessCurrentFrame();
                    
                    if (success)
                    {
                        lastCaptureTime = Time.time;
                        
                        if (debugMode)
                            Debug.Log("ARMLController: Successfully captured and processed frame");
                    }
                    else if (debugMode)
                    {
                        Debug.Log("ARMLController: Failed to capture and process frame");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ARMLController: Error during ML update: {e.Message}");
                // Continue execution to prevent the entire AR experience from stopping
            }
        }
    }

    /// <summary>
    /// Processes the current frame using the ML manager
    /// </summary>
    private bool ProcessCurrentFrame()
    {
        if (mlManager == null)
            return false;

        try
        {
            // Пробуем прямую передачу кадра в SegmentationManager
            if (segmentationManager != null && cameraManager != null)
            {
                // Эта часть будет выполняться через OnCameraFrameReceived,
                // но здесь мы оставляем совместимость со старым кодом
                return true;
            }
            
            // Используем MLManager в качестве адаптера
            if (mlManager.CaptureCurrentFrame())
            {
                return true;
            }
            
            // Запасной вариант через рефлексию для обратной совместимости
            System.Reflection.MethodInfo captureMethod = mlManager.GetType().GetMethod("CaptureCurrentFrame", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            
            if (captureMethod != null)
            {
                var result = captureMethod.Invoke(mlManager, null);
                if (result != null)
                {
                    // Successfully captured a frame
                    return true;
                }
            }
            
            // Try alternative method that might exist
            System.Reflection.MethodInfo predictMethod = mlManager.GetType().GetMethod("StartContinuousPrediction");
            if (predictMethod != null)
            {
                predictMethod.Invoke(mlManager, null);
                return true;
            }
            
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ARMLController: Error calling ML methods: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts the AR experience
    /// </summary>
    public void StartAR()
    {
        // Prevent multiple starts
        if (arStarted)
        {
            Debug.Log("ARMLController: AR already running, ignoring duplicate start request");
            return;
        }
        
        if (!IsSessionReady())
        {
            Debug.LogWarning("AR Session not ready yet. Please try again later.");
            if (!waitingForSession)
            {
                StartCoroutine(WaitForSessionAndStart());
            }
            return;
        }
        
        // Enable plane detection if we have a plane manager
        TogglePlaneDetection(true);
        
        // Set prediction interval and start ML predictions
        if (mlManager != null)
        {
            // Try to set prediction interval via reflection
            try
            {
                var setPredictionMethod = mlManager.GetType().GetMethod("SetPredictionInterval");
                if (setPredictionMethod != null)
                {
                    setPredictionMethod.Invoke(mlManager, new object[] { predictionInterval });
                }
                
                var startPredictionMethod = mlManager.GetType().GetMethod("StartContinuousPrediction");
                if (startPredictionMethod != null)
                {
                    startPredictionMethod.Invoke(mlManager, null);
                    Debug.Log("ARMLController: Started continuous prediction");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ARMLController: Error starting ML predictions: {e.Message}");
            }
        }
        
        arStarted = true;
        isRunning = true;
    }
    
    /// <summary>
    /// Stops the AR experience
    /// </summary>
    public void StopAR()
    {
        if (!arStarted)
            return;
            
        TogglePlaneDetection(false);
        
        if (mlManager != null)
        {
            try
            {
                var stopPredictionMethod = mlManager.GetType().GetMethod("StopContinuousPrediction");
                if (stopPredictionMethod != null)
                {
                    stopPredictionMethod.Invoke(mlManager, null);
                    Debug.Log("ARMLController: Stopped continuous prediction");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ARMLController: Error stopping ML predictions: {e.Message}");
            }
        }
        
        arStarted = false;
        isRunning = false;
    }
    
    /// <summary>
    /// Checks if the AR session is ready
    /// </summary>
    public bool IsSessionReady()
    {
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
        }
        
        return arSession != null && arSession.enabled && arSession.gameObject.activeInHierarchy;
    }
    
    /// <summary>
    /// Toggles plane detection on or off
    /// </summary>
    public void TogglePlaneDetection(bool enable)
    {
        if (arPlaneManager == null)
        {
            arPlaneManager = FindObjectOfType<ARPlaneManager>();
        }
        
        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = enable;
            Debug.Log($"ARMLController: Plane detection {(enable ? "enabled" : "disabled")}");
        }
    }
    
    /// <summary>
    /// Waits for the AR session to be ready before starting AR
    /// </summary>
    private IEnumerator WaitForSessionAndStart()
    {
        if (waitingForSession)
            yield break;
            
        waitingForSession = true;
        Debug.Log("ARMLController: Waiting for AR session to be ready...");
        
        float startTime = Time.time;
        while (!IsSessionReady() && Time.time - startTime < maxWaitTimeForSession)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        if (IsSessionReady())
        {
            Debug.Log("ARMLController: AR session is now ready, starting AR");
            StartAR();
        }
        else
        {
            Debug.LogWarning($"ARMLController: Timed out waiting for AR session (waited {maxWaitTimeForSession} seconds)");
        }
        
        waitingForSession = false;
    }

    /// <summary>
    /// Validates that all required references are present
    /// </summary>
    /// <returns>True if all references are valid, false otherwise</returns>
    private bool ValidateReferences()
    {
        bool allReferencesValid = true;
        
        // Try to find references if they're missing
        if (mlManager == null)
        {
            mlManager = FindObjectOfType<ARMLManagerAdapter>();
            if (mlManager == null)
            {
                // Создаем MLManager если его нет
                GameObject mlManagerObj = new GameObject("AR ML Manager Adapter");
                mlManager = mlManagerObj.AddComponent<ARMLManagerAdapter>();
                
                if (debugMode)
                    Debug.Log("ARMLController: Created ARMLManagerAdapter component");
            }
        }

        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
            if (cameraManager == null)
            {
                if (Time.frameCount % 300 == 0 || debugMode)
                    Debug.LogWarning("ARMLController: ARCameraManager reference is missing");
                allReferencesValid = false;
            }
        }
        
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
            if (arSession == null)
            {
                if (Time.frameCount % 300 == 0 || debugMode)
                    Debug.LogWarning("ARMLController: ARSession reference is missing");
                allReferencesValid = false;
            }
        }
        
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager == null)
            {
                if (Time.frameCount % 300 == 0 || debugMode)
                    Debug.LogWarning("ARMLController: SegmentationManager reference is missing");
                allReferencesValid = false;
            }
            else if (mlManager != null)
            {
                // Связываем MLManager с SegmentationManager
                mlManager.segmentationManager = segmentationManager;
            }
        }
        else if (mlManager != null && mlManager.segmentationManager == null)
        {
            // Связываем MLManager с SegmentationManager
            mlManager.segmentationManager = segmentationManager;
        }
        
        if (maskProcessor == null)
        {
            maskProcessor = FindObjectOfType<MaskProcessor>();
            if (maskProcessor == null && debugMode)
            {
                if (Time.frameCount % 300 == 0 || debugMode)
                    Debug.LogWarning("ARMLController: MaskProcessor reference is missing");
                // Optional - don't fail validation for this
            }
        }
        
        // For ARPlaneManager, just try to find it but don't fail validation
        if (arPlaneManager == null)
        {
            arPlaneManager = FindObjectOfType<ARPlaneManager>();
        }

        // If we depend on the enhanced predictor, ensure it exists
        if (useEnhancedPredictor && enhancedPredictor == null)
        {
            // Try to get it from scene
            enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
            // If still null, log warning
            if (enhancedPredictor == null)
            {
                if (Time.frameCount % 300 == 0 || debugMode)
                    Debug.LogWarning("ARMLController: EnhancedDeepLabPredictor reference is missing");
                
                // Only fail validation if we're explicitly using enhanced predictor
                if (useEnhancedPredictor)
                {
                    allReferencesValid = false;
                }
            }
        }

        return allReferencesValid;
    }
} 