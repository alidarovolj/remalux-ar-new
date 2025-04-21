using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(Camera))]
public class WallColorizer : MonoBehaviour
{
    [Header("Visualization")]
    public RawImage displayImage;
    public Camera arCamera;
    public Material wallMaterial;
    public Color currentColor = new Color(0.5f, 0.8f, 1f, 1f);
    public float wallOpacity = 0.7f;
    
    [Header("Runtime References")]
    public RenderTexture wallMaskRT;
    
    [Header("Wall Detection Settings")]
    [Range(0.001f, 0.1f)]
    [SerializeField] private float maskThreshold = 0.03f;
    [Range(0f, 1f)]
    [SerializeField] private float edgeEnhancement = 1.2f;
    [SerializeField] private bool useDepthTest = true;
    [SerializeField] private bool stabilizeWalls = true;
    [SerializeField] private int stabilizationFrames = 3;
    
    private RenderTexture cameraView;
    private bool isInitialized = false;
    
    [Header("Debug")]
    [SerializeField] private bool debugMode = false;
    
    #pragma warning disable 0414 // Suppress "unused field" warnings
    [SerializeField] private bool visualizeMask = false;
    #pragma warning restore 0414
    
    [SerializeField] private bool logPixelCounts = false;
    [SerializeField] private int minWallPixels = 100;
    
    // Internal properties
    private RenderTexture wallVisRT;
    private RenderTexture[] previousMasks;
    private int currentMaskIndex = 0;
    
    // Shader property IDs for faster lookup
    private int wallColorPropID;
    private int wallOpacityPropID;
    private int wallMaskPropID;
    private int maskThresholdPropID;
    private int edgeEnhancePropID;
    private int useDepthTestPropID;
    private int cameraPropID;
    
    // Wall detection statistics
    private int totalPixels = 0;
    private int wallPixels = 0;
    
    #pragma warning disable 0414 // Suppress "unused field" warnings
    private bool hasValidMask = false;
    #pragma warning restore 0414
    
    void Start()
    {
        try
        {
            // Auto-find components if missing
            if (displayImage == null)
            {
                Debug.LogWarning("WallColorizer: Display Image not assigned, trying to find one...");
                displayImage = GameObject.Find("AR Display")?.GetComponent<RawImage>();
                
                if (displayImage == null)
                {
                    // Try canvas hierarchy
                    Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                    foreach (Canvas canvas in canvases)
                    {
                        Transform displayTransform = canvas.transform.Find("AR Display");
                        if (displayTransform != null)
                        {
                            displayImage = displayTransform.GetComponent<RawImage>();
                            if (displayImage != null)
                            {
                                Debug.Log("WallColorizer: Found Display Image through canvas hierarchy");
                                break;
                            }
                        }
                    }
                    
                    // Last resort - any RawImage
                    if (displayImage == null)
                    {
                        displayImage = FindFirstObjectByType<RawImage>();
                        if (displayImage != null)
                        {
                            Debug.Log("WallColorizer: Using first available RawImage as display");
                        }
                    }
                }
            }
            
            if (displayImage == null)
            {
                Debug.LogError("WallColorizer: Display Image not assigned");
                return;
            }
            
            // Get AR Camera
            if (arCamera == null)
            {
                arCamera = Camera.main;
                if (arCamera == null)
                {
                    arCamera = GameObject.Find("AR Camera")?.GetComponent<Camera>();
                    if (arCamera == null)
                    {
                        arCamera = FindFirstObjectByType<Camera>();
                    }
                }
                
                if (arCamera == null)
                {
                    Debug.LogError("WallColorizer: AR Camera not found");
                    return;
                }
            }
            
            // Create render textures with proper format
            int width = Screen.width;
            int height = Screen.height;
            
            // Create camera view texture
            cameraView = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cameraView.antiAliasing = 1;
            cameraView.filterMode = FilterMode.Bilinear;
            cameraView.Create();
            
            // Create wall mask texture if not assigned
            if (wallMaskRT == null)
            {
                wallMaskRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                wallMaskRT.antiAliasing = 1;
                wallMaskRT.filterMode = FilterMode.Bilinear;
                wallMaskRT.Create();
            }
            
            // Initialize mask stabilization
            if (stabilizeWalls)
            {
                previousMasks = new RenderTexture[stabilizationFrames];
                for (int i = 0; i < stabilizationFrames; i++)
                {
                    previousMasks[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                    previousMasks[i].antiAliasing = 1;
                    previousMasks[i].filterMode = FilterMode.Bilinear;
                    previousMasks[i].Create();
                    
                    // Clear initially
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = previousMasks[i];
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = prev;
                }
            }
            
            // Cache shader property IDs
            wallColorPropID = Shader.PropertyToID("_Color");
            wallOpacityPropID = Shader.PropertyToID("_Opacity");
            wallMaskPropID = Shader.PropertyToID("_MaskTex");
            maskThresholdPropID = Shader.PropertyToID("_Threshold");
            edgeEnhancePropID = Shader.PropertyToID("_EdgeEnhance");
            useDepthTestPropID = Shader.PropertyToID("_UseDepthTest");
            cameraPropID = Shader.PropertyToID("_MainTex");
            
            // Set material properties
            if (wallMaterial != null)
            {
                try
                {
                    wallMaterial.SetColor(wallColorPropID, currentColor);
                    wallMaterial.SetFloat(wallOpacityPropID, wallOpacity);
                    wallMaterial.SetFloat(maskThresholdPropID, maskThreshold);
                    wallMaterial.SetFloat(edgeEnhancePropID, edgeEnhancement);
                    wallMaterial.SetInt(useDepthTestPropID, useDepthTest ? 1 : 0);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"WallColorizer: Failed to set material properties: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("WallColorizer: Wall material not assigned");
            }
            
            isInitialized = true;
            Debug.Log("WallColorizer initialized successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error during initialization: {e.Message}\n{e.StackTrace}");
        }
    }

    void Update()
    {
        if (!isInitialized)
            return;
            
        try
        {
            // Capture camera view
            if (arCamera != null && cameraView != null)
            {
                RenderTexture prevTarget = arCamera.targetTexture;
                arCamera.targetTexture = cameraView;
                arCamera.Render();
                arCamera.targetTexture = prevTarget;
                
                // Display camera view if no wall mask is available
                if (displayImage != null && displayImage.texture == null)
                {
                    displayImage.texture = cameraView;
                }
            }
            
            // Apply wall color if mask is available
            if (wallMaskRT != null)
            {
                ApplyWallColor();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error during update: {e.Message}");
        }
    }
    
    private void ValidateWallMask()
    {
        if (wallMaskRT == null || !wallMaskRT.IsCreated())
        {
            hasValidMask = false;
            if (debugMode)
            {
                Debug.LogWarning("WallColorizer: Mask texture is null or not created");
            }
            return;
        }
        
        // Quick check for completely empty masks (optimization)
        if (logPixelCounts || debugMode)
        {
            Texture2D tempTex = new Texture2D(wallMaskRT.width, wallMaskRT.height, TextureFormat.RGBA32, false);
            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = wallMaskRT;
            tempTex.ReadPixels(new Rect(0, 0, wallMaskRT.width, wallMaskRT.height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = prevRT;

            Color32[] pixels = tempTex.GetPixels32();
            totalPixels = pixels.Length;
            wallPixels = 0;
            
            // Count non-transparent pixels
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r > 10) // Check for non-black pixels
                {
                    wallPixels++;
                }
            }
            
            if (logPixelCounts)
            {
                Debug.Log($"Wall pixels found: {wallPixels} out of {totalPixels}");
            }
            
            if (wallPixels < minWallPixels)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"WallColorizer: Mask texture appears to be empty (only {wallPixels} wall pixels)");
                }
                hasValidMask = false;
            }
            else
            {
                hasValidMask = true;
            }
            
            Destroy(tempTex);
        }
        else
        {
            // If not counting pixels, just assume mask is valid
            hasValidMask = true;
        }
    }
    
    private void ApplyWallColor()
    {
        try
        {
            if (displayImage == null || cameraView == null || wallMaskRT == null || wallMaterial == null)
            {
                if (displayImage == null) Debug.LogError("WallColorizer: Display Image is null");
                if (cameraView == null) Debug.LogError("WallColorizer: Camera View is null");
                if (wallMaskRT == null) Debug.LogError("WallColorizer: Wall Mask is null");
                if (wallMaterial == null) Debug.LogError("WallColorizer: Wall Material is null");
                return;
            }
            
            // Update material parameters
            wallMaterial.SetFloat(maskThresholdPropID, maskThreshold);
            wallMaterial.SetFloat(edgeEnhancePropID, edgeEnhancement);
            wallMaterial.SetInt(useDepthTestPropID, useDepthTest ? 1 : 0);
            
            // Use stabilized mask if enabled
            RenderTexture maskToUse = wallMaskRT;
            if (stabilizeWalls && previousMasks != null && previousMasks.Length > 0)
            {
                // Store current mask in buffer
                Graphics.Blit(wallMaskRT, previousMasks[currentMaskIndex]);
                
                // Create a temporary render texture for blending masks
                RenderTexture blendedMask = RenderTexture.GetTemporary(
                    wallMaskRT.width, wallMaskRT.height, 0, RenderTextureFormat.ARGB32);
                
                // Clear the blended mask first
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = blendedMask;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;
                
                // Simple additive blending of the previous masks
                Material blendMat = new Material(Shader.Find("Hidden/Internal-GUITexture"));
                blendMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                blendMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                
                for (int i = 0; i < previousMasks.Length; i++)
                {
                    // Blend with lower weight for older frames
                    float weight = 1.0f - (0.3f * i / previousMasks.Length);
                    blendMat.SetFloat("_Alpha", weight);
                    Graphics.Blit(previousMasks[i], blendedMask, blendMat);
                }
                
                // Use the blended mask
                maskToUse = blendedMask;
                
                // Update the current index for the circular buffer
                currentMaskIndex = (currentMaskIndex + 1) % previousMasks.Length;
            }
            
            // Set mask texture for blending
            wallMaterial.SetTexture(cameraPropID, cameraView);
            wallMaterial.SetTexture(wallMaskPropID, maskToUse);
            wallMaterial.SetColor(wallColorPropID, currentColor);
            wallMaterial.SetFloat(wallOpacityPropID, wallOpacity);
            
            // Create temporary render texture for blending
            RenderTexture blendResult = RenderTexture.GetTemporary(
                cameraView.width, cameraView.height, 0, RenderTextureFormat.ARGB32);
                
            // Apply wall material with mask to blend camera and colored wall
            Graphics.Blit(cameraView, blendResult, wallMaterial);
            
            // Set result as display texture
            displayImage.texture = blendResult;
            
            // Release temporary textures
            if (stabilizeWalls && maskToUse != wallMaskRT)
            {
                RenderTexture.ReleaseTemporary(maskToUse);
            }
            RenderTexture.ReleaseTemporary(blendResult);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error applying wall color: {e.Message}");
        }
    }

    /// <summary>
    /// Set the wall mask texture
    /// </summary>
    public void SetWallMask(RenderTexture maskTexture)
    {
        try
        {
            if (maskTexture == null)
            {
                Debug.LogWarning("WallColorizer: Null mask texture received");
                return;
            }
            
            // Copy to our wallMaskRT
            if (wallMaskRT == null || wallMaskRT.width != maskTexture.width || wallMaskRT.height != maskTexture.height)
            {
                if (wallMaskRT != null)
                    wallMaskRT.Release();
                    
                wallMaskRT = new RenderTexture(maskTexture.width, maskTexture.height, 0, RenderTextureFormat.ARGB32);
                wallMaskRT.antiAliasing = 1;
                wallMaskRT.filterMode = FilterMode.Bilinear;
                wallMaskRT.Create();
                
                // If we're using stabilization, recreate those buffers too
                if (stabilizeWalls && previousMasks != null)
                {
                    for (int i = 0; i < previousMasks.Length; i++)
                    {
                        if (previousMasks[i] != null)
                            previousMasks[i].Release();
                            
                        previousMasks[i] = new RenderTexture(maskTexture.width, maskTexture.height, 0, RenderTextureFormat.ARGB32);
                        previousMasks[i].antiAliasing = 1;
                        previousMasks[i].filterMode = FilterMode.Bilinear;
                        previousMasks[i].Create();
                        
                        // Clear initially
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = previousMasks[i];
                        GL.Clear(true, true, Color.clear);
                        RenderTexture.active = prev;
                    }
                }
            }
            
            // Copy mask to our render texture
            Graphics.Blit(maskTexture, wallMaskRT);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error setting wall mask: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set the wall color
    /// </summary>
    public void SetWallColor(Color color)
    {
        currentColor = color;
        
        if (wallMaterial != null)
        {
            try
            {
                wallMaterial.SetColor(wallColorPropID, currentColor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to set wall color: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Set the wall color (compatibility method for ARMLController)
    /// </summary>
    public void SetColor(Color color)
    {
        SetWallColor(color);
    }
    
    /// <summary>
    /// Set the wall opacity
    /// </summary>
    public void SetWallOpacity(float opacity)
    {
        if (!isInitialized)
            return;
            
        try
        {
            wallOpacity = Mathf.Clamp01(opacity);
            
            if (wallMaterial != null)
            {
                wallMaterial.SetFloat(wallOpacityPropID, wallOpacity);
                if (debugMode)
                {
                    Debug.Log($"Wall opacity set to {wallOpacity}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error setting opacity: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set the mask threshold
    /// </summary>
    public void SetMaskThreshold(float threshold)
    {
        maskThreshold = Mathf.Clamp(threshold, 0.001f, 0.1f);
        
        if (wallMaterial != null)
        {
            try
            {
                wallMaterial.SetFloat(maskThresholdPropID, maskThreshold);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to set mask threshold: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Toggle depth testing for more accurate wall placement
    /// </summary>
    public void ToggleDepthTest(bool enable)
    {
        useDepthTest = enable;
        
        if (wallMaterial != null)
        {
            try
            {
                wallMaterial.SetInt(useDepthTestPropID, useDepthTest ? 1 : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to toggle depth test: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Toggle wall mask stabilization
    /// </summary>
    public void ToggleStabilization(bool enable)
    {
        // Can't enable if it was off and buffers weren't created
        if (enable && !stabilizeWalls && previousMasks == null)
        {
            InitializeStabilization();
        }
        
        stabilizeWalls = enable;
    }
    
    /// <summary>
    /// Initialize stabilization buffers
    /// </summary>
    private void InitializeStabilization()
    {
        if (wallMaskRT == null)
            return;
            
        previousMasks = new RenderTexture[stabilizationFrames];
        for (int i = 0; i < stabilizationFrames; i++)
        {
            previousMasks[i] = new RenderTexture(wallMaskRT.width, wallMaskRT.height, 0, RenderTextureFormat.ARGB32);
            previousMasks[i].antiAliasing = 1;
            previousMasks[i].filterMode = FilterMode.Bilinear;
            previousMasks[i].Create();
            
            // Clear initially
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = previousMasks[i];
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }
    }
    
    /// <summary>
    /// Clear the wall visualization
    /// </summary>
    public void ClearVisualization()
    {
        try
        {
            // Clear the current wall mask
            if (wallMaskRT != null && wallMaskRT.IsCreated())
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = wallMaskRT;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;
            }
            
            // Also clear any stabilization buffers
            if (stabilizeWalls && previousMasks != null)
            {
                foreach (var mask in previousMasks)
                {
                    if (mask != null && mask.IsCreated())
                    {
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = mask;
                        GL.Clear(true, true, Color.clear);
                        RenderTexture.active = prev;
                    }
                }
            }
            
            // Make sure display shows the camera view
            if (displayImage != null && cameraView != null)
            {
                displayImage.texture = cameraView;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallColorizer: Error clearing visualization: {e.Message}");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up render textures
        if (cameraView != null)
        {
            cameraView.Release();
            Destroy(cameraView);
        }
        
        if (wallMaskRT != null)
        {
            wallMaskRT.Release();
            Destroy(wallMaskRT);
        }
        
        if (previousMasks != null)
        {
            foreach (var mask in previousMasks)
            {
                if (mask != null)
                {
                    mask.Release();
                    Destroy(mask);
                }
            }
        }
    }

    // Adding method to fix CS1061 error in AppController.cs
    public void SetOpacity(float opacity)
    {
        // Simply call the existing SetWallOpacity method
        SetWallOpacity(opacity);
    }

    // Adding method to fix CS1061 error in AppController.cs
    public void ClearWalls()
    {
        // Clear the wall visualization
        ClearVisualization();
        
        // Clear the wall mask
        if (wallMaskRT != null)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = wallMaskRT;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }
        
        // Also clear stabilization textures if they exist
        if (previousMasks != null)
        {
            for (int i = 0; i < previousMasks.Length; i++)
            {
                if (previousMasks[i] != null)
                {
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = previousMasks[i];
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = prev;
                }
            }
        }
        
        if (debugMode)
        {
            Debug.Log("Wall visualization cleared");
        }
    }
} 