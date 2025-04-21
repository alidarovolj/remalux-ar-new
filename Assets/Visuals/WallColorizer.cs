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
    
    // Shader property IDs for faster lookup
    private int wallColorPropID;
    private int wallOpacityPropID;
    private int wallMaskPropID;
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
            
            // Set material properties
            if (wallMaterial != null)
            {
                try
                {
                    wallMaterial.SetColor("_Color", currentColor);
                    wallMaterial.SetFloat("_Opacity", wallOpacity);
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
            
            // Set mask texture for blending
            wallMaterial.SetTexture("_MainTex", cameraView);
            wallMaterial.SetTexture("_MaskTex", wallMaskRT);
            wallMaterial.SetColor("_Color", currentColor);
            wallMaterial.SetFloat("_Opacity", wallOpacity);
            
            // Create temporary render texture for blending
            RenderTexture blendResult = RenderTexture.GetTemporary(
                cameraView.width, cameraView.height, 0, RenderTextureFormat.ARGB32);
                
            // Apply wall material with mask to blend camera and colored wall
            Graphics.Blit(cameraView, blendResult, wallMaterial);
            
            // Set result as display texture
            displayImage.texture = blendResult;
            
            // Release temporary texture
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
                wallMaterial.SetColor("_Color", currentColor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to set wall color: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Set the wall opacity
    /// </summary>
    public void SetWallOpacity(float opacity)
    {
        wallOpacity = Mathf.Clamp01(opacity);
        
        if (wallMaterial != null)
        {
            try
            {
                wallMaterial.SetFloat("_Opacity", wallOpacity);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to set wall opacity: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Clear the wall visualization
    /// </summary>
    public void ClearVisualization()
    {
        if (displayImage != null && cameraView != null)
        {
            displayImage.texture = cameraView;
        }
    }
    
    void OnDestroy()
    {
        // Release render textures
        if (cameraView != null)
        {
            cameraView.Release();
            cameraView = null;
        }
        
        if (wallMaskRT != null)
        {
            wallMaskRT.Release();
            wallMaskRT = null;
        }
    }
    
    // Compatibility methods for existing code
    
    /// <summary>
    /// Set wall color - compatibility alias for SetWallColor
    /// </summary>
    public void SetColor(Color color)
    {
        SetWallColor(color);
    }
    
    /// <summary>
    /// Set wall opacity - compatibility alias for SetWallOpacity
    /// </summary>
    public void SetOpacity(float opacity)
    {
        SetWallOpacity(opacity);
    }
    
    /// <summary>
    /// Clear wall visualization - compatibility alias for ClearVisualization
    /// </summary>
    public void ClearWalls()
    {
        ClearVisualization();
    }
} 