using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System;
using ML.DeepLab;

/// <summary>
/// Connects ML components with AR components, handling event communication
/// between segmentation, mask processing, and wall painting.
/// </summary>
public class ARMLSystemConnector : MonoBehaviour
{
    [Header("Required Components")]
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private MaskProcessor maskProcessor;
    [SerializeField] private ARWallPainterController wallPainterController;
    
    [Header("Settings")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool useEnhancedPredictor = true;
    
    // Debug visualization
    [SerializeField] private UnityEngine.UI.RawImage debugRawImage;
    
    private void Awake()
    {
        // Find components automatically if not assigned
        if (arCameraManager == null)
            arCameraManager = FindAnyObjectByType<ARCameraManager>();
            
        if (segmentationManager == null)
            segmentationManager = FindAnyObjectByType<SegmentationManager>();
            
        if (maskProcessor == null)
            maskProcessor = FindAnyObjectByType<MaskProcessor>();
            
        if (wallPainterController == null)
            wallPainterController = FindAnyObjectByType<ARWallPainterController>();
            
        // Log warnings for missing components
        if (arCameraManager == null)
            Debug.LogError("ARMLSystemConnector: ARCameraManager not found");
            
        if (segmentationManager == null)
            Debug.LogError("ARMLSystemConnector: SegmentationManager not found");
            
        if (maskProcessor == null)
            Debug.LogError("ARMLSystemConnector: MaskProcessor not found");
            
        if (wallPainterController == null)
            Debug.LogError("ARMLSystemConnector: ARWallPainterController not found");
    }
    
    private void OnEnable()
    {
        // Subscribe to events when enabled
        if (segmentationManager != null)
        {
            segmentationManager.OnSegmentationCompleted += HandleSegmentationTexture;
            Debug.Log("ARMLSystemConnector: Subscribed to SegmentationManager events");
        }
        
        if (maskProcessor != null)
        {
            maskProcessor.OnMaskProcessed += OnMaskProcessed;
            Debug.Log("ARMLSystemConnector: Subscribed to MaskProcessor events");
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from events when disabled
        if (segmentationManager != null)
            segmentationManager.OnSegmentationCompleted -= HandleSegmentationTexture;
            
        if (maskProcessor != null)
            maskProcessor.OnMaskProcessed -= OnMaskProcessed;
    }
    
    /// <summary>
    /// Handler for segmentation completion with Texture2D
    /// </summary>
    private void HandleSegmentationTexture(Texture2D segmentationTexture)
    {
        if (segmentationTexture == null) return;
        
        // Convert to RenderTexture for processing
        RenderTexture rt = new RenderTexture(
            segmentationTexture.width,
            segmentationTexture.height,
            0,
            RenderTextureFormat.ARGB32
        );
        rt.enableRandomWrite = true;
        rt.Create();
        
        Graphics.Blit(segmentationTexture, rt);
        
        // Call our standard handler
        OnSegmentationCompleted(rt);
    }
    
    /// <summary>
    /// Handler for segmentation completion with byte and Texture2D signature
    /// </summary>
    private void HandleSegmentationCompleted(byte wallClassId, Texture2D segmentationTexture)
    {
        HandleSegmentationTexture(segmentationTexture);
    }
    
    /// <summary>
    /// Handler for segmentation completion
    /// </summary>
    private void OnSegmentationCompleted(RenderTexture segmentationTexture)
    {
        if (debugMode)
            Debug.Log("ARMLSystemConnector: Segmentation completed, forwarding to mask processor");
        
        // Forward the segmentation texture to the mask processor
        if (maskProcessor != null)
        {
            maskProcessor.ProcessMask(segmentationTexture);
        }
        
        // Optional debug visualization
        if (debugMode && debugRawImage != null)
        {
            debugRawImage.texture = segmentationTexture;
            debugRawImage.gameObject.SetActive(true);
        }
    }
    
    /// <summary>
    /// Handler for mask processing completion
    /// </summary>
    private void OnMaskProcessed(RenderTexture processedMask)
    {
        if (debugMode)
            Debug.Log("ARMLSystemConnector: Mask processed, forwarding to wall painter");
        
        // Forward the processed mask to the wall painter
        if (wallPainterController != null)
        {
            // Use reflection to call the protected method
            var methodInfo = wallPainterController.GetType().GetMethod(
                "OnMaskProcessed", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
                
            if (methodInfo != null)
            {
                methodInfo.Invoke(wallPainterController, new object[] { processedMask });
                
                if (debugMode)
                    Debug.Log("ARMLSystemConnector: Successfully called OnMaskProcessed via reflection");
            }
            else if (debugMode)
            {
                Debug.LogWarning("ARMLSystemConnector: Could not find OnMaskProcessed method");
            }
        }
        
        // Optional debug visualization
        if (debugMode && debugRawImage != null)
        {
            debugRawImage.texture = processedMask;
        }
    }
    
    /// <summary>
    /// Force a rescan of wall surfaces
    /// </summary>
    public void RescanWalls()
    {
        if (segmentationManager != null)
        {
            ProcessCameraFrameNow();
            Debug.Log("ARMLSystemConnector: Forced wall rescan");
        }
    }
    
    /// <summary>
    /// Immediately process the current camera frame
    /// </summary>
    public void ProcessCameraFrameNow()
    {
        if (segmentationManager != null && arCameraManager != null)
        {
            // Capture frame from camera
            Texture2D cameraTexture = CaptureARCameraFrame();
            
            if (cameraTexture != null)
            {
                // Process the frame through segmentation with target resolution
                Vector2Int targetResolution = new Vector2Int(cameraTexture.width, cameraTexture.height);
                segmentationManager.ProcessCameraFrame(cameraTexture, targetResolution);
                
                if (debugMode)
                    Debug.Log("ARMLSystemConnector: Camera frame captured and sent for processing");
            }
            else if (debugMode)
            {
                Debug.LogWarning("ARMLSystemConnector: Failed to capture camera frame");
            }
        }
    }
    
    /// <summary>
    /// Capture the current frame from AR camera
    /// </summary>
    private Texture2D CaptureARCameraFrame()
    {
        if (arCameraManager == null || arCameraManager.GetComponent<Camera>() == null)
            return null;
            
        Camera arCamera = arCameraManager.GetComponent<Camera>();
        
        // Create texture for camera frame
        int width = 512; 
        int height = 512;
        
        // Adjust size based on device capabilities
        if (SystemInfo.graphicsMemorySize > 1024)
        {
            width = 1024;
            height = 1024;
        }
        
        // Create render texture and CPU-readable texture
        RenderTexture rt = new RenderTexture(width, height, 24);
        Texture2D frameTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        
        // Render camera view to texture
        RenderTexture prevRT = arCamera.targetTexture;
        arCamera.targetTexture = rt;
        arCamera.Render();
        arCamera.targetTexture = prevRT;
        
        // Read pixels
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        frameTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        frameTexture.Apply();
        RenderTexture.active = prevActive;
        
        rt.Release();
        return frameTexture;
    }
    
    /// <summary>
    /// Reset temporal smoothing when camera moves significantly
    /// </summary>
    public void ResetSmoothing()
    {
        if (maskProcessor != null)
        {
            maskProcessor.ResetTemporalSmoothing();
            Debug.Log("ARMLSystemConnector: Reset temporal smoothing");
        }
    }
} 