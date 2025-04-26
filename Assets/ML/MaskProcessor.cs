using UnityEngine;
using System;

/// <summary>
/// Processes segmentation masks from ML models
/// </summary>
public class MaskProcessor : MonoBehaviour
{
    [SerializeField] private bool debugMode = false;
    [SerializeField] private Material postProcessingMaterial;
    [SerializeField] private float temporalSmoothingFactor = 0.7f;
    
    private Texture2D previousMask;
    private RenderTexture previousRenderMask;
    private bool temporalSmoothingEnabled = true;
    
    // Events
    public event Action<Texture2D> OnMaskProcessed;
    
    // Additional event with RenderTexture parameter for systems that prefer it
    public event Action<RenderTexture> OnRenderTextureMaskProcessed;
    
    public void ProcessMask(Texture2D segmentationMask)
    {
        if (segmentationMask == null)
        {
            Debug.LogError("MaskProcessor: Cannot process null segmentation mask");
            return;
        }
        
        if (debugMode)
            Debug.Log($"MaskProcessor: Processing mask {segmentationMask.width}x{segmentationMask.height}");
        
        // Apply any needed processing
        
        // Create a RenderTexture version
        RenderTexture renderTextureMask = new RenderTexture(segmentationMask.width, segmentationMask.height, 0, RenderTextureFormat.ARGB32);
        renderTextureMask.enableRandomWrite = true;
        renderTextureMask.Create();
        
        Graphics.Blit(segmentationMask, renderTextureMask);
        
        // Apply temporal smoothing if enabled
        if (temporalSmoothingEnabled && previousRenderMask != null)
        {
            ApplyTemporalSmoothing(renderTextureMask);
        }
        
        // Store for next frame's temporal smoothing
        if (previousRenderMask != null)
            RenderTexture.ReleaseTemporary(previousRenderMask);
            
        previousRenderMask = RenderTexture.GetTemporary(renderTextureMask.width, renderTextureMask.height, 0, renderTextureMask.format);
        Graphics.Blit(renderTextureMask, previousRenderMask);
        
        // Notify listeners for both Texture2D and RenderTexture
        OnMaskProcessed?.Invoke(segmentationMask);
        OnRenderTextureMaskProcessed?.Invoke(renderTextureMask);
    }
    
    public void ProcessMask(RenderTexture segmentationMask)
    {
        if (segmentationMask == null)
        {
            Debug.LogError("MaskProcessor: Cannot process null segmentation RenderTexture");
            return;
        }
        
        if (debugMode)
            Debug.Log($"MaskProcessor: Processing render texture mask {segmentationMask.width}x{segmentationMask.height}");
        
        // Apply temporal smoothing if enabled
        if (temporalSmoothingEnabled && previousRenderMask != null)
        {
            ApplyTemporalSmoothing(segmentationMask);
        }
        
        // Store for next frame's temporal smoothing
        if (previousRenderMask != null)
            RenderTexture.ReleaseTemporary(previousRenderMask);
            
        previousRenderMask = RenderTexture.GetTemporary(segmentationMask.width, segmentationMask.height, 0, segmentationMask.format);
        Graphics.Blit(segmentationMask, previousRenderMask);
        
        // Convert RenderTexture to Texture2D for systems that expect that
        Texture2D resultTexture = ConvertRenderTextureToTexture2D(segmentationMask);
        
        // Notify listeners for both Texture2D and RenderTexture formats
        if (resultTexture != null)
            OnMaskProcessed?.Invoke(resultTexture);
            
        OnRenderTextureMaskProcessed?.Invoke(segmentationMask);
    }
    
    /// <summary>
    /// Resets the temporal smoothing state, useful when camera viewpoint changes significantly
    /// </summary>
    public void ResetTemporalSmoothing()
    {
        if (debugMode)
            Debug.Log("MaskProcessor: Temporal smoothing reset");
            
        // Clear previous mask references to reset temporal smoothing
        if (previousMask != null)
        {
            Destroy(previousMask);
            previousMask = null;
        }
        
        if (previousRenderMask != null)
        {
            RenderTexture.ReleaseTemporary(previousRenderMask);
            previousRenderMask = null;
        }
    }
    
    /// <summary>
    /// Enable or disable temporal smoothing
    /// </summary>
    public void SetTemporalSmoothingEnabled(bool enabled)
    {
        temporalSmoothingEnabled = enabled;
        
        if (!enabled)
            ResetTemporalSmoothing();
            
        if (debugMode)
            Debug.Log($"MaskProcessor: Temporal smoothing {(enabled ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Apply temporal smoothing between the current and previous mask
    /// </summary>
    private void ApplyTemporalSmoothing(RenderTexture currentMask)
    {
        if (postProcessingMaterial != null)
        {
            // Use a shader with lerp between current and previous
            postProcessingMaterial.SetFloat("_SmoothingFactor", temporalSmoothingFactor);
            postProcessingMaterial.SetTexture("_PreviousTex", previousRenderMask);
            
            // Blit with the smoothing material
            RenderTexture tempRT = RenderTexture.GetTemporary(currentMask.width, currentMask.height, 0, currentMask.format);
            Graphics.Blit(currentMask, tempRT, postProcessingMaterial);
            Graphics.Blit(tempRT, currentMask);
            RenderTexture.ReleaseTemporary(tempRT);
        }
        else
        {
            // Simple CPU-based smoothing as fallback
            if (debugMode)
                Debug.LogWarning("MaskProcessor: No smoothing material assigned, temporal smoothing may be less efficient");
        }
    }
    
    private Texture2D ConvertRenderTextureToTexture2D(RenderTexture rt)
    {
        // Create a new texture of the same dimensions
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        
        // Store the active render texture
        RenderTexture previousRT = RenderTexture.active;
        
        // Set the given render texture as active
        RenderTexture.active = rt;
        
        // Read pixels from the render texture to the new texture
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        
        // Restore the previously active render texture
        RenderTexture.active = previousRT;
        
        return tex;
    }
    
    private void OnDestroy()
    {
        ResetTemporalSmoothing();
    }
} 