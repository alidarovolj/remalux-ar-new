using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Main controller for AR wall painting application.
/// Orchestrates the AR tracking, segmentation, mask processing, and paint rendering.
/// </summary>
public class ARWallPainterController : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private ARWallSystem arSystem;
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private MaskProcessor maskProcessor;
    [SerializeField] private PaintRenderer paintRendererPrefab;
    
    [Header("Paint Settings")]
    [SerializeField] private ColorPalette colorPalette;
    [SerializeField] private int defaultColorIndex = 0;
    [SerializeField] private PaintFinishType defaultFinish = PaintFinishType.Eggshell;
    
    [Header("Camera Settings")]
    [SerializeField] private int captureWidth = 1280;
    [SerializeField] private int captureHeight = 720;
    
    // State variables
    private Texture2D _cameraTexture;
    private Dictionary<TrackableId, PaintRenderer> _paintRenderers = new Dictionary<TrackableId, PaintRenderer>();
    private Color _currentPaintColor;
    private PaintFinishType _currentFinish;
    private RenderTexture _currentMask;
    
    // Optimization flags
    private bool _isProcessingFrame = false;
    private int _frameCounter = 0;
    
    private void Awake()
    {
        // Try to find ARWallSystem if not assigned
        if (arSystem == null)
            arSystem = FindObjectOfType<ARWallSystem>();
            
        // Create required textures
        _cameraTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        
        // Initialize state
        _currentPaintColor = colorPalette != null && colorPalette.paintColors.Count > defaultColorIndex
            ? colorPalette.paintColors[defaultColorIndex].color
            : Color.white;
            
        _currentFinish = defaultFinish;
    }
    
    private void OnEnable()
    {
        // Subscribe to events
        if (arSystem != null)
        {
            arSystem.OnARInitialized += OnARInitialized;
        }
        
        if (segmentationManager != null)
        {
            segmentationManager.OnSegmentationCompleted += OnSegmentationCompleted;
        }
        
        if (maskProcessor != null)
        {
            maskProcessor.OnMaskProcessed += OnMaskProcessed;
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from events
        if (arSystem != null)
        {
            arSystem.OnARInitialized -= OnARInitialized;
        }
        
        if (segmentationManager != null)
        {
            segmentationManager.OnSegmentationCompleted -= OnSegmentationCompleted;
        }
        
        if (maskProcessor != null)
        {
            maskProcessor.OnMaskProcessed -= OnMaskProcessed;
        }
    }
    
    private void Update()
    {
        _frameCounter++;
        
        // Skip processing if already in progress or if not every Nth frame
        if (_isProcessingFrame || _frameCounter % 3 != 0)
            return;
        
        CaptureCameraFrame();
    }
    
    /// <summary>
    /// Capture the current camera frame and send it for processing
    /// </summary>
    private void CaptureCameraFrame()
    {
        if (arSystem == null || arSystem.ARCamera == null || segmentationManager == null)
            return;
            
        _isProcessingFrame = true;
        
        // Get current render texture from camera
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture cameraRT = new RenderTexture(captureWidth, captureHeight, 24);
        
        // Render camera view to texture
        arSystem.ARCamera.targetTexture = cameraRT;
        arSystem.ARCamera.Render();
        arSystem.ARCamera.targetTexture = null;
        
        // Read pixels from render texture
        RenderTexture.active = cameraRT;
        _cameraTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        _cameraTexture.Apply();
        
        // Restore previous render texture
        RenderTexture.active = currentRT;
        
        // Send to segmentation
        segmentationManager.ProcessCameraFrame(_cameraTexture);
        
        // Clean up
        cameraRT.Release();
        _isProcessingFrame = false;
    }
    
    /// <summary>
    /// Called when AR system is initialized
    /// </summary>
    private void OnARInitialized()
    {
        // Use PlaneManager directly from arSystem instead of finding it
        if (arSystem != null && arSystem.PlaneManager != null)
        {
            arSystem.PlaneManager.planesChanged += OnPlanesChanged;
        }
        else
        {
            Debug.LogError("Cannot find ARPlaneManager. Wall detection will not work.");
        }
    }
    
    /// <summary>
    /// Called when segmentation is completed
    /// </summary>
    private void OnSegmentationCompleted(byte wallClassId, Texture2D segmentationResult)
    {
        // Send to mask processor
        // First convert to RenderTexture for compute shader processing
        RenderTexture rt = new RenderTexture(
            segmentationResult.width,
            segmentationResult.height,
            0,
            RenderTextureFormat.R8
        );
        rt.enableRandomWrite = true;
        rt.Create();
        
        Graphics.Blit(segmentationResult, rt);
        
        // Process the mask
        maskProcessor.ProcessMask(rt);
    }
    
    /// <summary>
    /// Called when mask processing is completed
    /// </summary>
    private void OnMaskProcessed(RenderTexture processedMask)
    {
        // Store current mask
        _currentMask = processedMask;
        
        // Update all paint renderers with new mask
        foreach (var renderer in _paintRenderers.Values)
        {
            renderer.UpdateMask(processedMask);
        }
    }
    
    /// <summary>
    /// Called when AR planes change
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Process added planes
        foreach (ARPlane plane in args.added)
        {
            if (IsVerticalPlane(plane))
            {
                CreatePaintRendererForPlane(plane);
            }
        }
        
        // Process updated planes
        foreach (ARPlane plane in args.updated)
        {
            if (IsVerticalPlane(plane))
            {
                UpdatePaintRendererForPlane(plane);
            }
        }
        
        // Process removed planes
        foreach (ARPlane plane in args.removed)
        {
            RemovePaintRendererForPlane(plane);
        }
    }
    
    /// <summary>
    /// Check if a plane is vertical (a wall)
    /// </summary>
    private bool IsVerticalPlane(ARPlane plane)
    {
        // Check alignment - vertical planes have normals roughly horizontal
        Vector3 planeNormal = plane.normal;
        float dotUp = Vector3.Dot(planeNormal, Vector3.up);
        
        // Vertical planes have dot product with up vector close to zero
        // Allow for some tolerance as real-world planes might not be perfectly vertical
        return Mathf.Abs(dotUp) < 0.25f; 
    }
    
    /// <summary>
    /// Create paint renderer for a plane
    /// </summary>
    private void CreatePaintRendererForPlane(ARPlane plane)
    {
        // Skip if we already have a renderer for this plane
        if (_paintRenderers.ContainsKey(plane.trackableId))
            return;
            
        // Create renderer object
        PaintRenderer renderer = Instantiate(paintRendererPrefab, plane.transform.position, plane.transform.rotation);
        renderer.SetPlaneGeometry(plane);
        
        // Configure renderer
        renderer.SetPaintColor(_currentPaintColor);
        renderer.SetPaintFinish(_currentFinish);
        
        // Initialize with current mask if available
        if (_currentMask != null)
        {
            renderer.Initialize(_currentMask);
        }
        
        // Store reference
        _paintRenderers[plane.trackableId] = renderer;
    }
    
    /// <summary>
    /// Update paint renderer for a plane
    /// </summary>
    private void UpdatePaintRendererForPlane(ARPlane plane)
    {
        // Skip if we don't have a renderer for this plane
        if (!_paintRenderers.TryGetValue(plane.trackableId, out PaintRenderer renderer))
            return;
            
        // Update geometry
        renderer.SetPlaneGeometry(plane);
    }
    
    /// <summary>
    /// Remove paint renderer for a plane
    /// </summary>
    private void RemovePaintRendererForPlane(ARPlane plane)
    {
        // Skip if we don't have a renderer for this plane
        if (!_paintRenderers.TryGetValue(plane.trackableId, out PaintRenderer renderer))
            return;
            
        // Destroy renderer
        Destroy(renderer.gameObject);
        
        // Remove reference
        _paintRenderers.Remove(plane.trackableId);
    }
    
    /// <summary>
    /// Set the current paint color
    /// </summary>
    public void SetPaintColor(Color color)
    {
        _currentPaintColor = color;
        
        // Update all renderers
        foreach (var renderer in _paintRenderers.Values)
        {
            renderer.SetPaintColor(color);
        }
    }
    
    /// <summary>
    /// Set the current paint color from palette
    /// </summary>
    public void SetPaintColor(int colorIndex)
    {
        if (colorPalette != null && colorIndex >= 0 && colorIndex < colorPalette.paintColors.Count)
        {
            SetPaintColor(colorPalette.paintColors[colorIndex].color);
        }
    }
    
    /// <summary>
    /// Set the current paint finish
    /// </summary>
    public void SetPaintFinish(PaintFinishType finish)
    {
        _currentFinish = finish;
        
        // Update all renderers
        foreach (var renderer in _paintRenderers.Values)
        {
            renderer.SetPaintFinish(finish);
        }
    }
    
    /// <summary>
    /// Set the current paint finish from index
    /// </summary>
    public void SetPaintFinish(int finishIndex)
    {
        if (colorPalette != null && finishIndex >= 0 && finishIndex < colorPalette.paintFinishes.Count)
        {
            SetPaintFinish(colorPalette.paintFinishes[finishIndex].type);
        }
    }
    
    /// <summary>
    /// Sample color from camera at screen position
    /// </summary>
    public void SampleColorFromScreen(Vector2 screenPosition)
    {
        if (_cameraTexture == null)
            return;
            
        // Convert screen position to camera texture coordinates
        Vector2 textureCoord = new Vector2(
            screenPosition.x / Screen.width * _cameraTexture.width,
            screenPosition.y / Screen.height * _cameraTexture.height
        );
        
        // Ensure coordinates are within texture bounds
        textureCoord.x = Mathf.Clamp(textureCoord.x, 0, _cameraTexture.width - 1);
        textureCoord.y = Mathf.Clamp(textureCoord.y, 0, _cameraTexture.height - 1);
        
        // Sample color
        Color sampledColor = _cameraTexture.GetPixel((int)textureCoord.x, (int)textureCoord.y);
        
        // Set as current color
        SetPaintColor(sampledColor);
    }
    
    /// <summary>
    /// Take a screenshot of the current view
    /// </summary>
    public void TakeScreenshot()
    {
        // Implementation could save to gallery, share, etc.
        StartCoroutine(CaptureScreenshot());
    }
    
    /// <summary>
    /// Reset the temporal smoothing (use when camera moves significantly)
    /// </summary>
    public void ResetTemporalSmoothing()
    {
        if (maskProcessor != null)
        {
            maskProcessor.ResetTemporalSmoothing();
        }
    }
    
    /// <summary>
    /// Coroutine for capturing screenshot
    /// </summary>
    private System.Collections.IEnumerator CaptureScreenshot()
    {
        // Wait for end of frame to capture everything
        yield return new WaitForEndOfFrame();
        
        // Create texture for screenshot
        Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        
        // Capture screen
        screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();
        
        // Save to device gallery (implementation depends on platform)
        // For example using NativeGallery plugin:
        // NativeGallery.SaveImageToGallery(screenshot, "AR Wall Painter", $"ARPaint_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
        
        // For Unity Editor testing
        #if UNITY_EDITOR
        byte[] bytes = screenshot.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/ARWallPainter_Screenshot.png", bytes);
        Debug.Log("Screenshot saved to: " + Application.dataPath + "/ARWallPainter_Screenshot.png");
        #endif
        
        // Clean up
        Destroy(screenshot);
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from all events
        if (arSystem != null && arSystem.PlaneManager != null)
        {
            arSystem.PlaneManager.planesChanged -= OnPlanesChanged;
        }
        
        // Clean up resources
        foreach (var renderer in _paintRenderers.Values)
        {
            if (renderer != null)
            {
                Destroy(renderer.gameObject);
            }
        }
        
        _paintRenderers.Clear();
        
        if (_cameraTexture != null)
        {
            Destroy(_cameraTexture);
        }
    }
} 