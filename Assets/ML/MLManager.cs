using UnityEngine;
using System;
using System.Collections;

public class MLManager : MonoBehaviour
{
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private float predictionInterval = 0.5f;
    
    private bool isProcessing = false;
    public event Action<RenderTexture> OnSegmentationComplete;
    
    private WallColorizer wallColorizer;
    
    private void Start()
    {
        if (deepLabPredictor == null)
        {
            Debug.LogError("DeepLabPredictor reference is missing!");
            enabled = false;
            return;
        }
    }
    
    public void SetPredictionInterval(float interval)
    {
        predictionInterval = interval;
    }
    
    public void StartContinuousPrediction()
    {
        if (!isProcessing)
        {
            isProcessing = true;
            StartCoroutine(PredictionLoop());
        }
    }
    
    public void StopContinuousPrediction()
    {
        isProcessing = false;
    }
    
    private IEnumerator PredictionLoop()
    {
        while (isProcessing)
        {
            // Capture current camera frame
            Texture2D cameraFrame = CaptureCurrentFrame();
            
            if (cameraFrame != null)
            {
                // Process frame through DeepLab
                RenderTexture segmentationMask = deepLabPredictor.PredictSegmentation(cameraFrame);
                
                // Notify listeners
                OnSegmentationComplete?.Invoke(segmentationMask);
                
                Destroy(cameraFrame);
            }
            
            yield return new WaitForSeconds(predictionInterval);
        }
    }
    
    private Texture2D CaptureCurrentFrame()
    {
        // Get the main camera
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return null;
        
        // Create a new render texture
        RenderTexture rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
        // Store current camera target
        RenderTexture previousTarget = mainCamera.targetTexture;
        
        // Set target and render
        mainCamera.targetTexture = rt;
        mainCamera.Render();
        
        // Restore camera's original target
        mainCamera.targetTexture = previousTarget;
        
        // Create a new texture and read pixels
        Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        
        // Store current active render texture
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        
        screenshot.ReadPixels(new UnityEngine.Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();
        
        // Restore previous active render texture
        RenderTexture.active = previousActive;
        
        // Release render texture
        RenderTexture.ReleaseTemporary(rt);
        
        return screenshot;
    }
} 