using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;

public class ARMLController : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private ARManager arManager;
    [SerializeField] private MLManager mlManager;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private WallColorizer wallColorizer;
    
    [Header("Settings")]
    [SerializeField] private bool autoStartAR = true;  // Всегда автоматический запуск
    [SerializeField] private float predictionInterval = 0.5f;
    [SerializeField] private float maxWaitTimeForSession = 10f; // Maximum time to wait for session in seconds
    
    private bool waitingForSession = false;
    private bool arStarted = false; // Track if AR is already started
    
    private void Start()
    {
        ValidateReferences();
        
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
            Debug.LogError("AR Manager reference is missing!");
            enabled = false;
            return;
        }
        
        if (mlManager == null)
        {
            Debug.LogError("ML Manager reference is missing!");
            enabled = false;
            return;
        }
        
        if (deepLabPredictor == null)
        {
            Debug.LogError("DeepLab Predictor reference is missing!");
            enabled = false;
            return;
        }
        
        if (wallColorizer == null)
        {
            Debug.LogError("Wall Colorizer reference is missing!");
            enabled = false;
            return;
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
} 