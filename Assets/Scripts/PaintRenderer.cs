using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Handles rendering of paint on walls with realistic lighting preservation.
/// Attaches to AR planes and manages materials with custom shaders.
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class PaintRenderer : MonoBehaviour
{
    [Header("Paint Settings")]
    [SerializeField] private Material basePaintMaterial;
    [SerializeField] private Color paintColor = Color.blue;
    [SerializeField] [Range(0, 1)] private float opacity = 0.8f;
    
    [Header("Finish Settings")]
    [SerializeField] [Range(0, 1)] private float glossiness = 0.2f;
    [SerializeField] [Range(0, 1)] private float metallic = 0;
    
    [Header("Occlusion")]
    [SerializeField] private bool useDepthBasedOcclusion = true;
    [SerializeField] private float depthOffset = 0.05f;
    
    // References
    private MeshRenderer _meshRenderer;
    private Material _instancedMaterial;
    private RenderTexture _maskTexture;
    private ARAnchor _anchor;
    
    // Shader property IDs (cached for performance)
    private static readonly int _colorProp = Shader.PropertyToID("_PaintColor");
    private static readonly int _opacityProp = Shader.PropertyToID("_Opacity");
    private static readonly int _glossinessProp = Shader.PropertyToID("_Glossiness");
    private static readonly int _metallicProp = Shader.PropertyToID("_Metallic");
    private static readonly int _maskTexProp = Shader.PropertyToID("_MaskTex");
    private static readonly int _thresholdProp = Shader.PropertyToID("_Threshold");
    private static readonly int _useDepthTestProp = Shader.PropertyToID("_UseDepthTest");
    private static readonly int _depthOffsetProp = Shader.PropertyToID("_DepthOffset");
    
    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        
        // Create material instance if needed
        if (basePaintMaterial != null)
        {
            // Create a material instance to avoid modifying the shared material
            _instancedMaterial = new Material(basePaintMaterial);
            _meshRenderer.material = _instancedMaterial;
            
            // Initialize material properties
            UpdateMaterialProperties();
        }
        else
        {
            Debug.LogError("PaintRenderer: Base paint material is not assigned!");
        }
    }
    
    /// <summary>
    /// Initialize the paint renderer with a mask texture and anchor
    /// </summary>
    public void Initialize(RenderTexture maskTexture, ARAnchor anchor = null)
    {
        if (_instancedMaterial == null)
        {
            Debug.LogError("PaintRenderer: Cannot initialize - material is null");
            return;
        }
        
        // Store references
        _maskTexture = maskTexture;
        _anchor = anchor;
        
        // Apply mask texture to material
        _instancedMaterial.SetTexture(_maskTexProp, _maskTexture);
        
        // Make sure the renderer is enabled
        _meshRenderer.enabled = true;
    }
    
    /// <summary>
    /// Set wall geometry from an ARPlane
    /// </summary>
    public void SetPlaneGeometry(ARPlane plane)
    {
        if (plane == null) return;
        
        // Get the mesh filter
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) return;
        
        // Copy the plane mesh
        meshFilter.mesh = plane.GetComponent<MeshFilter>()?.mesh;
        
        // Adjust transform to match plane
        transform.position = plane.transform.position;
        transform.rotation = plane.transform.rotation;
        transform.localScale = plane.transform.localScale;
    }
    
    /// <summary>
    /// Update the mask texture (when new segmentation is available)
    /// </summary>
    public void UpdateMask(RenderTexture newMask)
    {
        if (_instancedMaterial == null) return;
        
        _maskTexture = newMask;
        _instancedMaterial.SetTexture(_maskTexProp, _maskTexture);
    }
    
    /// <summary>
    /// Set paint color
    /// </summary>
    public void SetPaintColor(Color color)
    {
        paintColor = color;
        
        // Update material
        if (_instancedMaterial != null)
        {
            _instancedMaterial.SetColor(_colorProp, paintColor);
        }
    }
    
    /// <summary>
    /// Set paint opacity
    /// </summary>
    public void SetOpacity(float value)
    {
        opacity = Mathf.Clamp01(value);
        
        // Update material
        if (_instancedMaterial != null)
        {
            _instancedMaterial.SetFloat(_opacityProp, opacity);
        }
    }
    
    /// <summary>
    /// Set paint finish (glossiness and metallic values)
    /// </summary>
    public void SetPaintFinish(float newGlossiness, float newMetallic)
    {
        glossiness = Mathf.Clamp01(newGlossiness);
        metallic = Mathf.Clamp01(newMetallic);
        
        UpdateMaterialProperties();
    }
    
    /// <summary>
    /// Set a preset paint finish
    /// </summary>
    public void SetPaintFinish(PaintFinishType finishType)
    {
        switch (finishType)
        {
            case PaintFinishType.Matte:
                glossiness = 0.05f;
                metallic = 0.0f;
                break;
                
            case PaintFinishType.Eggshell:
                glossiness = 0.2f;
                metallic = 0.0f;
                break;
                
            case PaintFinishType.SemiGloss:
                glossiness = 0.5f;
                metallic = 0.05f;
                break;
                
            case PaintFinishType.Gloss:
                glossiness = 0.9f;
                metallic = 0.1f;
                break;
        }
        
        UpdateMaterialProperties();
    }
    
    /// <summary>
    /// Enable/disable depth-based occlusion
    /// </summary>
    public void SetDepthOcclusion(bool enabled)
    {
        useDepthBasedOcclusion = enabled;
        
        if (_instancedMaterial != null)
        {
            _instancedMaterial.SetInt(_useDepthTestProp, useDepthBasedOcclusion ? 1 : 0);
        }
    }
    
    /// <summary>
    /// Update all material properties based on current settings
    /// </summary>
    private void UpdateMaterialProperties()
    {
        if (_instancedMaterial == null) return;
        
        _instancedMaterial.SetColor(_colorProp, paintColor);
        _instancedMaterial.SetFloat(_opacityProp, opacity);
        _instancedMaterial.SetFloat(_glossinessProp, glossiness);
        _instancedMaterial.SetFloat(_metallicProp, metallic);
        _instancedMaterial.SetFloat(_thresholdProp, 0.1f); // Default threshold
        _instancedMaterial.SetInt(_useDepthTestProp, useDepthBasedOcclusion ? 1 : 0);
        _instancedMaterial.SetFloat(_depthOffsetProp, depthOffset);
    }
    
    /// <summary>
    /// Clean up resources when destroyed
    /// </summary>
    private void OnDestroy()
    {
        // Clean up instanced material
        if (_instancedMaterial != null)
        {
            Destroy(_instancedMaterial);
        }
    }
}

/// <summary>
/// Types of paint finish
/// </summary>
public enum PaintFinishType
{
    Matte,
    Eggshell,
    SemiGloss,
    Gloss
} 