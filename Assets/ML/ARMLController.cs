using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor

/// <summary>
/// Advanced ML controller for AR applications
/// </summary>
public class AdvancedARMLController : MonoBehaviour
{
    /// <summary>
    /// The interval between ML captures
    /// </summary>
    [SerializeField] private float predictionInterval = 1f;

    /// <summary>
    /// Whether to run the ML processing
    /// </summary>
    [SerializeField] private bool isRunning = true;

    /// <summary>
    /// Whether to enable debug mode
    /// </summary>
    [SerializeField] private bool debugMode = false;

    /// <summary>
    /// Whether to use the enhanced predictor
    /// </summary>
    [SerializeField] private bool useEnhancedPredictor = true;

    /// <summary>
    /// The last time a frame was captured
    /// </summary>
    private float lastCaptureTime = 0f;

    /// <summary>
    /// The ML manager
    /// </summary>
    private MLManager mlManager;

    /// <summary>
    /// The camera manager
    /// </summary>
    private ARCameraManager cameraManager;

    /// <summary>
    /// The enhanced predictor
    /// </summary>
    private EnhancedDeepLabPredictor enhancedPredictor;

    [Header("Component References")]
    [SerializeField] private ARManager arManager;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private WallColorizer wallColorizer;
    
    [Header("Settings")]
    [SerializeField] private bool autoStartAR = true;  // Всегда автоматический запуск
    [SerializeField] private float maxWaitTimeForSession = 10f; // Maximum time to wait for session in seconds
    
    private bool waitingForSession = false;
    private bool arStarted = false; // Track if AR is already started
    
    private void Start()
    {
        // Initialize components
        EnsureValidReferences();
        
        if (autoStartAR)
        {
            // Try to start AR, or wait for session to be ready
            if (!TryStartAR())
            {
                StartCoroutine(WaitForSessionAndStart());
            }
        }
        
        // Subscribe to ML events
        if (mlManager != null)
        {
            mlManager.OnSegmentationComplete += HandleSegmentationResult;
            Debug.Log("ARMLController: Subscribed to segmentation events");
        }
    }
    
    private bool TryStartAR()
    {
        // Prevent multiple starts
        if (arStarted)
        {
            Debug.Log("ARMLController: AR already running");
            return true;
        }
        
        if (arManager == null || !arManager.IsSessionReady())
        {
            return false;
        }
        
        // Enable plane detection
        arManager.TogglePlaneDetection(true);
        
        // Set prediction interval and start ML predictions
        if (mlManager != null)
        {
            mlManager.SetPredictionInterval(predictionInterval);
            mlManager.StartContinuousPrediction();
            Debug.Log("ARMLController: Started continuous prediction");
        }
        
        arStarted = true;
        return true;
    }
    
    private IEnumerator WaitForSessionAndStart()
    {
        if (waitingForSession)
            yield break;
            
        waitingForSession = true;
        Debug.Log("ARMLController: Waiting for AR session to be ready...");
        
        float startTime = Time.time;
        while (!arManager.IsSessionReady() && Time.time - startTime < maxWaitTimeForSession)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        if (arManager.IsSessionReady())
        {
            Debug.Log("ARMLController: AR session is now ready, starting AR");
            TryStartAR();
        }
        else
        {
            Debug.LogWarning($"ARMLController: Timed out waiting for AR session (waited {maxWaitTimeForSession} seconds)");
        }
        
        waitingForSession = false;
    }
    
    private void ValidateReferences()
    {
        if (arManager == null)
        {
            arManager = FindObjectOfType<ARManager>();
            if (arManager == null)
            {
                Debug.LogError("AR Manager reference is missing!");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("ARMLController: Found ARManager in scene");
            }
        }
        
        if (mlManager == null)
        {
            mlManager = FindObjectOfType<MLManager>();
            if (mlManager == null)
            {
                Debug.LogError("ML Manager reference is missing!");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("ARMLController: Found MLManager in scene");
            }
        }
        
        if (deepLabPredictor == null)
        {
            deepLabPredictor = FindObjectOfType<DeepLabPredictor>();
            if (deepLabPredictor == null)
            {
                Debug.LogError("DeepLab Predictor reference is missing!");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("ARMLController: Found DeepLabPredictor in scene");
            }
        }
        
        if (wallColorizer == null)
        {
            wallColorizer = FindObjectOfType<WallColorizer>();
            if (wallColorizer == null)
            {
                Debug.LogError("Wall Colorizer reference is missing!");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("ARMLController: Found WallColorizer in scene");
            }
        }

        Debug.Log("ARMLController: All references validated successfully");
    }
    
    public void StartAR()
    {
        // Prevent multiple starts
        if (arStarted)
        {
            Debug.Log("ARMLController: AR already running, ignoring duplicate start request");
            return;
        }
        
        if (!arManager.IsSessionReady())
        {
            Debug.LogWarning("AR Session not ready yet. Please try again later.");
            if (!waitingForSession)
            {
                StartCoroutine(WaitForSessionAndStart());
            }
            return;
        }
        
        // Enable plane detection
        arManager.TogglePlaneDetection(true);
        
        // Set prediction interval and start ML predictions
        mlManager.SetPredictionInterval(predictionInterval);
        mlManager.StartContinuousPrediction();
        Debug.Log("ARMLController: Started continuous prediction");
        
        arStarted = true;
    }
    
    public void StopAR()
    {
        if (!arStarted)
            return;
            
        arManager.TogglePlaneDetection(false);
        mlManager.StopContinuousPrediction();
        Debug.Log("ARMLController: Stopped continuous prediction");
        
        arStarted = false;
    }
    
    private void HandleSegmentationResult(RenderTexture segmentationMask)
    {
        if (segmentationMask == null)
        {
            Debug.LogWarning("ARMLController: Received null segmentation mask");
            return;
        }

        Debug.Log($"ARMLController: Received segmentation mask with size {segmentationMask.width}x{segmentationMask.height}");
        
        // Update wall visualization
        if (wallColorizer != null)
        {
            wallColorizer.SetWallMask(segmentationMask);
            Debug.Log("ARMLController: Updated wall mask in colorizer");
        }
        else
        {
            Debug.LogWarning("ARMLController: WallColorizer is null");
        }
    }
    
    public void SetWallColor(Color color)
    {
        if (wallColorizer != null)
        {
            wallColorizer.SetColor(color);
            Debug.Log($"ARMLController: Set wall color to {color}");
        }
    }
    
    private void OnDestroy()
    {
        if (mlManager != null)
        {
            mlManager.OnSegmentationComplete -= HandleSegmentationResult;
        }
    }

    private void Update()
    {
        // Run ML processing if active
        if (isRunning)
        {
            UpdateML();
        }
    }

    /// <summary>
    /// Updates the ML processing
    /// </summary>
    private void UpdateML()
    {
        // Make sure we have valid components
        if (!EnsureValidReferences())
        {
            if (Time.frameCount % 300 == 0) // Only log occasionally to avoid spam
            {
                Debug.LogWarning("AdvancedARMLController: Missing required references for ML processing");
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
                    // Try to get the CaptureCurrentFrame method which we know exists in MLManager
                    MethodInfo captureMethod = mlManager.GetType().GetMethod("CaptureCurrentFrame", 
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    
                    if (captureMethod != null)
                    {
                        Texture2D frameTexture = captureMethod.Invoke(mlManager, null) as Texture2D;
                        if (frameTexture != null)
                        {
                            // Now try to process this texture with the predictor
                            ProcessCapturedTexture(frameTexture);
                            
                            // Clean up
                            Destroy(frameTexture);
                            
                            // Update last capture time
                            lastCaptureTime = Time.time;
                            
                            if (debugMode)
                                Debug.Log("AdvancedARMLController: Successfully captured and processed frame");
                        }
                        else if (debugMode)
                        {
                            Debug.Log("AdvancedARMLController: Captured null texture");
                        }
                    }
                    else if (debugMode)
                    {
                        Debug.LogWarning("AdvancedARMLController: CaptureCurrentFrame method not found in MLManager");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"AdvancedARMLController: Error during ML update: {e.Message}");
                // Continue execution to prevent the entire AR experience from stopping
            }
        }
    }

    /// <summary>
    /// Process a captured texture using available predictors
    /// </summary>
    private void ProcessCapturedTexture(Texture2D texture)
    {
        if (texture == null || mlManager == null) return;
        
        // Try to start continuous prediction in ML Manager, if available
        MethodInfo startPredictionMethod = mlManager.GetType().GetMethod("StartContinuousPrediction");
        if (startPredictionMethod != null)
        {
            startPredictionMethod.Invoke(mlManager, null);
        }
    }

    /// <summary>
    /// Validates that all required references are present
    /// </summary>
    /// <returns>True if all references are valid, false otherwise</returns>
    private bool EnsureValidReferences()
    {
        // Try to find references if they're missing
        if (mlManager == null)
        {
            mlManager = FindObjectOfType<MLManager>();
            if (mlManager == null && debugMode)
            {
                Debug.LogWarning("AdvancedARMLController: MLManager reference is missing");
                return false;
            }
        }

        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
            if (cameraManager == null && debugMode)
            {
                Debug.LogWarning("AdvancedARMLController: ARCameraManager reference is missing");
                return false;
            }
        }

        // If we depend on the enhanced predictor, ensure it exists
        if (useEnhancedPredictor)
        {
            // Try to get the predictor from ML Manager
            var predictorComponent = mlManager != null ? mlManager.GetComponent(System.Type.GetType("EnhancedDeepLabPredictor")) : null;
            
            if (predictorComponent == null && debugMode)
            {
                Debug.LogWarning("AdvancedARMLController: EnhancedDeepLabPredictor reference is missing");
                // Don't return false here as we can still work with the standard predictor
            }
        }

        return mlManager != null && cameraManager != null;
    }
} 