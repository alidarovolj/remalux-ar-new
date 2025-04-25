using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Handles post-processing of segmentation masks using compute shaders for efficiency.
/// Performs morphological operations, contour detection and temporal smoothing.
/// </summary>
public class MaskProcessor : MonoBehaviour
{
    [Header("Compute Shader References")]
    [SerializeField] private ComputeShader morphologyShader;
    [SerializeField] private ComputeShader contourShader;
    [SerializeField] private ComputeShader temporalSmoothingShader;
    
    [Header("Processing Settings")]
    [SerializeField] private int morphologyKernelSize = 3;
    [SerializeField] private int erosionIterations = 1;
    [SerializeField] private int dilationIterations = 2;
    [SerializeField] [Range(0, 1)] private float temporalSmoothingFactor = 0.2f;
    [SerializeField] private bool enableTemporalSmoothing = true;
    
    [Header("Debug")]
    [SerializeField] private bool saveDebugTextures = false;
    
    // Processing buffers (reused to minimize GC)
    private RenderTexture _erodedMask;
    private RenderTexture _dilatedMask;
    private RenderTexture _processedMask;
    private RenderTexture _previousMask;
    private RenderTexture _temporalMask;
    
    // Kernel IDs for compute shaders
    private int _erodeKernel;
    private int _dilateKernel;
    private int _contourKernel;
    private int _temporalSmoothingKernel;
    
    // Event fired when mask processing is complete
    public event Action<RenderTexture> OnMaskProcessed;
    
    // Flag to track first frame for temporal smoothing
    private bool _isFirstFrame = true;
    
    private void Awake()
    {
        // Find kernel IDs
        if (morphologyShader != null)
        {
            _erodeKernel = morphologyShader.FindKernel("Erode");
            _dilateKernel = morphologyShader.FindKernel("Dilate");
        }
        
        if (contourShader != null)
        {
            _contourKernel = contourShader.FindKernel("DetectContours");
        }
        
        if (temporalSmoothingShader != null)
        {
            _temporalSmoothingKernel = temporalSmoothingShader.FindKernel("TemporalSmoothing");
        }
    }
    
    /// <summary>
    /// Process a raw segmentation mask through morphological operations
    /// </summary>
    public void ProcessMask(RenderTexture rawMask)
    {
        if (rawMask == null)
        {
            Debug.LogError("MaskProcessor: Received null raw mask!");
            return;
        }
        
        // Ensure processing textures are initialized with matching dimensions
        EnsureTexturesInitialized(rawMask.width, rawMask.height);
        
        // Apply erosion to remove small noise
        ApplyErosion(rawMask, _erodedMask, erosionIterations);
        
        // Apply dilation to fill holes
        ApplyDilation(_erodedMask, _dilatedMask, dilationIterations);
        
        // Apply temporal smoothing if enabled and not the first frame
        if (enableTemporalSmoothing && !_isFirstFrame)
        {
            ApplyTemporalSmoothing(_dilatedMask, _previousMask, _temporalMask);
            
            // Store current mask for next frame
            Graphics.Blit(_temporalMask, _previousMask);
            
            // Use temporally smoothed mask as final output
            Graphics.Blit(_temporalMask, _processedMask);
        }
        else
        {
            // Without temporal smoothing, use dilated mask directly
            Graphics.Blit(_dilatedMask, _processedMask);
            
            // Initialize temporal processing for next frames
            if (_isFirstFrame && enableTemporalSmoothing)
            {
                Graphics.Blit(_dilatedMask, _previousMask);
                _isFirstFrame = false;
            }
        }
        
        // Notify listeners
        OnMaskProcessed?.Invoke(_processedMask);
    }
    
    /// <summary>
    /// Create or resize processing textures as needed
    /// </summary>
    private void EnsureTexturesInitialized(int width, int height)
    {
        // Helper to create/update a RenderTexture
        Action<ref RenderTexture> createRT = (ref RenderTexture rt) => {
            if (rt == null || rt.width != width || rt.height != height)
            {
                if (rt != null)
                    rt.Release();
                
                rt = new RenderTexture(width, height, 0, RenderTextureFormat.R8);
                rt.enableRandomWrite = true;
                rt.Create();
            }
        };
        
        // Create all required textures
        createRT(ref _erodedMask);
        createRT(ref _dilatedMask);
        createRT(ref _processedMask);
        createRT(ref _previousMask);
        createRT(ref _temporalMask);
    }
    
    /// <summary>
    /// Apply erosion operation using compute shader
    /// </summary>
    private void ApplyErosion(RenderTexture source, RenderTexture destination, int iterations)
    {
        if (morphologyShader == null)
        {
            // Fallback if shader missing
            Graphics.Blit(source, destination);
            return;
        }
        
        // Use intermediate textures for multiple iterations
        RenderTexture currentSource = source;
        RenderTexture currentDest = destination;
        
        // Setup shader parameters
        morphologyShader.SetInt("_KernelSize", morphologyKernelSize);
        
        for (int i = 0; i < iterations; i++)
        {
            // Setup textures
            morphologyShader.SetTexture(_erodeKernel, "Source", currentSource);
            morphologyShader.SetTexture(_erodeKernel, "Result", currentDest);
            
            // Calculate dispatch size (groups of 8x8)
            int threadGroupsX = Mathf.CeilToInt(currentSource.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(currentSource.height / 8.0f);
            
            // Execute shader
            morphologyShader.Dispatch(_erodeKernel, threadGroupsX, threadGroupsY, 1);
            
            // For multiple iterations, we need an intermediate texture
            if (i < iterations - 1)
            {
                RenderTexture temp = RenderTexture.GetTemporary(
                    currentSource.width, 
                    currentSource.height, 
                    0, 
                    RenderTextureFormat.R8
                );
                temp.enableRandomWrite = true;
                
                Graphics.Blit(currentDest, temp);
                currentSource = temp;
                
                // Release temporary textures from previous iterations
                if (i > 0)
                    RenderTexture.ReleaseTemporary(currentSource);
            }
        }
    }
    
    /// <summary>
    /// Apply dilation operation using compute shader
    /// </summary>
    private void ApplyDilation(RenderTexture source, RenderTexture destination, int iterations)
    {
        if (morphologyShader == null)
        {
            // Fallback if shader missing
            Graphics.Blit(source, destination);
            return;
        }
        
        // Use intermediate textures for multiple iterations
        RenderTexture currentSource = source;
        RenderTexture currentDest = destination;
        
        // Setup shader parameters
        morphologyShader.SetInt("_KernelSize", morphologyKernelSize);
        
        for (int i = 0; i < iterations; i++)
        {
            // Setup textures
            morphologyShader.SetTexture(_dilateKernel, "Source", currentSource);
            morphologyShader.SetTexture(_dilateKernel, "Result", currentDest);
            
            // Calculate dispatch size (groups of 8x8)
            int threadGroupsX = Mathf.CeilToInt(currentSource.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(currentSource.height / 8.0f);
            
            // Execute shader
            morphologyShader.Dispatch(_dilateKernel, threadGroupsX, threadGroupsY, 1);
            
            // For multiple iterations, we need an intermediate texture
            if (i < iterations - 1)
            {
                RenderTexture temp = RenderTexture.GetTemporary(
                    currentSource.width, 
                    currentSource.height, 
                    0, 
                    RenderTextureFormat.R8
                );
                temp.enableRandomWrite = true;
                
                Graphics.Blit(currentDest, temp);
                currentSource = temp;
                
                // Release temporary textures from previous iterations
                if (i > 0)
                    RenderTexture.ReleaseTemporary(currentSource);
            }
        }
    }
    
    /// <summary>
    /// Apply temporal smoothing between frames
    /// </summary>
    private void ApplyTemporalSmoothing(RenderTexture current, RenderTexture previous, RenderTexture result)
    {
        if (temporalSmoothingShader == null)
        {
            // Fallback to simple copy
            Graphics.Blit(current, result);
            return;
        }
        
        // Setup shader
        temporalSmoothingShader.SetTexture(_temporalSmoothingKernel, "CurrentFrame", current);
        temporalSmoothingShader.SetTexture(_temporalSmoothingKernel, "PreviousFrame", previous);
        temporalSmoothingShader.SetTexture(_temporalSmoothingKernel, "Result", result);
        temporalSmoothingShader.SetFloat("_BlendFactor", temporalSmoothingFactor);
        
        // Calculate dispatch size
        int threadGroupsX = Mathf.CeilToInt(current.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(current.height / 8.0f);
        
        // Execute shader
        temporalSmoothingShader.Dispatch(_temporalSmoothingKernel, threadGroupsX, threadGroupsY, 1);
    }
    
    /// <summary>
    /// Reset temporal smoothing (e.g., when camera moves significantly)
    /// </summary>
    public void ResetTemporalSmoothing()
    {
        _isFirstFrame = true;
    }
    
    /// <summary>
    /// Set the temporal smoothing factor at runtime
    /// </summary>
    public void SetTemporalSmoothingFactor(float factor)
    {
        temporalSmoothingFactor = Mathf.Clamp01(factor);
    }
    
    private void OnDestroy()
    {
        // Clean up render textures
        if (_erodedMask != null) _erodedMask.Release();
        if (_dilatedMask != null) _dilatedMask.Release();
        if (_processedMask != null) _processedMask.Release();
        if (_previousMask != null) _previousMask.Release();
        if (_temporalMask != null) _temporalMask.Release();
    }
    
#if UNITY_EDITOR
    [ContextMenu("Create Default Compute Shaders")]
    public void CreateDefaultComputeShaders()
    {
        Debug.Log("This would create default compute shaders if implemented");
        // In actual implementation, this would create default shaders for the project
    }
#endif
} 