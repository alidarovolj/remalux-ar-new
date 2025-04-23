using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab;

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
    private MLManager mlManager;

    /// <summary>
    /// The camera manager
    /// </summary>
    private ARCameraManager cameraManager;

    /// <summary>
    /// Reference to the AR session
    /// </summary>
    private ARSession arSession;

    /// <summary>
    /// Reference to AR Plane Manager for plane detection
    /// </summary>
    private ARPlaneManager arPlaneManager;

    /// <summary>
    /// The enhanced predictor
    /// </summary>
    private EnhancedDeepLabPredictor enhancedPredictor;
    
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

    private void Start()
    {
        // Validate references when component starts
        ValidateReferences();
    }

    private void Update()
    {
        // Update ML processing if enabled
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
            // Call methods via reflection to handle incompatible APIs
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
        // Try to find references if they're missing
        if (mlManager == null)
        {
            mlManager = FindObjectOfType<MLManager>();
            if (mlManager == null && debugMode)
            {
                Debug.LogWarning("ARMLController: MLManager reference is missing");
                return false;
            }
        }

        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
            if (cameraManager == null && debugMode)
            {
                Debug.LogWarning("ARMLController: ARCameraManager reference is missing");
                return false;
            }
        }
        
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
        }
        
        if (arPlaneManager == null)
        {
            arPlaneManager = FindObjectOfType<ARPlaneManager>();
        }

        // If we depend on the enhanced predictor, ensure it exists
        if (useEnhancedPredictor && enhancedPredictor == null)
        {
            // Try to get it from the MLManager
            if (mlManager != null)
            {
                enhancedPredictor = mlManager.GetComponent<EnhancedDeepLabPredictor>();
            }
            
            // If still null, log warning
            if (enhancedPredictor == null && debugMode)
            {
                Debug.LogWarning("ARMLController: EnhancedDeepLabPredictor reference is missing");
                // Don't return false here as we can still work with the standard predictor
            }
        }

        return mlManager != null && cameraManager != null;
    }
} 