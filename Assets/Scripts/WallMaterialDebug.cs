using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Компонент для отладки материалов стен
/// </summary>
public class WallMaterialDebug : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text _debugText;
    [SerializeField] private GameObject _debugPanel;
    
    [Header("Settings")]
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private bool _monitorMaterials = true;
    [SerializeField] private float _updateInterval = 1.0f;
    
    // Reference to component
    private ARPlaneManager _planeManager;
    private ARWallPainter _wallPainter;
    
    // Debug data
    private float _lastUpdateTime;
    private Dictionary<Material, int> _materialUsage = new Dictionary<Material, int>();
    private Dictionary<Shader, int> _shaderUsage = new Dictionary<Shader, int>();
    
    private void Start()
    {
        // Find components
        _planeManager = FindObjectOfType<ARPlaneManager>();
        _wallPainter = FindObjectOfType<ARWallPainter>();
        
        // Show/hide debug panel
        if (_debugPanel != null)
        {
            _debugPanel.SetActive(_showDebugInfo);
        }
    }
    
    private void Update()
    {
        // Only update at intervals
        if (Time.time - _lastUpdateTime < _updateInterval)
            return;
            
        _lastUpdateTime = Time.time;
        
        if (_monitorMaterials && _showDebugInfo && _debugText != null)
        {
            UpdateDebugText();
        }
    }
    
    /// <summary>
    /// Update the debug text with material information
    /// </summary>
    private void UpdateDebugText()
    {
        if (_planeManager == null || _debugText == null)
            return;
            
        // Clear material usage data
        _materialUsage.Clear();
        _shaderUsage.Clear();
        
        // Count materials and shaders in planes
        int planeCount = 0;
        int visiblePlaneCount = 0;
        int wallPlaneCount = 0;
        
        foreach (ARPlane plane in _planeManager.trackables)
        {
            planeCount++;
            
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (renderer.enabled)
                {
                    visiblePlaneCount++;
                }
                
                Material mat = renderer.sharedMaterial;
                if (mat != null)
                {
                    // Count material usage
                    if (_materialUsage.ContainsKey(mat))
                    {
                        _materialUsage[mat]++;
                    }
                    else
                    {
                        _materialUsage[mat] = 1;
                    }
                    
                    // Count shader usage
                    Shader shader = mat.shader;
                    if (shader != null)
                    {
                        if (_shaderUsage.ContainsKey(shader))
                        {
                            _shaderUsage[shader]++;
                        }
                        else
                        {
                            _shaderUsage[shader] = 1;
                        }
                    }
                    
                    // Check if it's likely a wall material
                    if (mat.color.r > 0.3f && mat.color.g > 0.3f && mat.color.b > 0.3f)
                    {
                        wallPlaneCount++;
                    }
                }
            }
        }
        
        // Build debug text
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Planes: {planeCount} (visible: {visiblePlaneCount}, walls: {wallPlaneCount})");
        
        // Wall painter info
        if (_wallPainter != null)
        {
            sb.AppendLine($"WallPainter material: {(_wallPainter._wallMaterial != null ? _wallPainter._wallMaterial.name : "NULL")}");
            
            if (_wallPainter._wallMaterial != null && _wallPainter._wallMaterial.shader != null)
            {
                sb.AppendLine($"WallPainter shader: {_wallPainter._wallMaterial.shader.name}");
            }
            else
            {
                sb.AppendLine("WallPainter shader: NULL");
            }
        }
        
        // Materials
        sb.AppendLine("\nMaterials in use:");
        foreach (var entry in _materialUsage)
        {
            string matName = entry.Key != null ? entry.Key.name : "NULL";
            sb.AppendLine($"- {matName}: {entry.Value} plane(s)");
        }
        
        // Shaders
        sb.AppendLine("\nShaders in use:");
        foreach (var entry in _shaderUsage)
        {
            string shaderName = entry.Key != null ? entry.Key.name : "NULL";
            sb.AppendLine($"- {shaderName}: {entry.Value} plane(s)");
        }
        
        // Update debug text
        _debugText.text = sb.ToString();
    }
    
    /// <summary>
    /// Toggle debug panel
    /// </summary>
    public void ToggleDebugPanel()
    {
        _showDebugInfo = !_showDebugInfo;
        
        if (_debugPanel != null)
        {
            _debugPanel.SetActive(_showDebugInfo);
        }
    }
    
    /// <summary>
    /// Fix materials using the WallMaterialHelper
    /// </summary>
    public void FixMaterials()
    {
        WallMaterialHelper.Instance.FixWallMaterials();
        UpdateDebugText();
    }
} 