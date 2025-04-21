using UnityEngine;

/// <summary>
/// Вспомогательный класс для управления материалами стен
/// </summary>
public class WallMaterialHelper : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private Material _fallbackMaterial;
    
    [Header("Settings")]
    [SerializeField] private bool _autoFix = true;
    [SerializeField] private bool _debugMode = true;
    
    // Singleton instance
    private static WallMaterialHelper _instance;
    public static WallMaterialHelper Instance 
    { 
        get 
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<WallMaterialHelper>();
                
                if (_instance == null)
                {
                    GameObject go = new GameObject("WallMaterialHelper");
                    _instance = go.AddComponent<WallMaterialHelper>();
                }
            }
            
            return _instance;
        }
    }
    
    private void Awake()
    {
        // Ensure singleton
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Create fallback material if needed
        if (_fallbackMaterial == null)
        {
            CreateFallbackMaterial();
        }
        
        // Initialize WallMeshMaterialFixer
        if (_autoFix)
        {
            FixWallMaterials();
        }
    }
    
    /// <summary>
    /// Get the proper wall material (with fallback if needed)
    /// </summary>
    public Material GetWallMaterial()
    {
        // Check if wall material is valid
        if (_wallMaterial != null && _wallMaterial.shader != null)
        {
            if (_debugMode)
                Debug.Log($"WallMaterialHelper: Using wall material: {_wallMaterial.name} with shader: {_wallMaterial.shader.name}");
            
            return _wallMaterial;
        }
        
        // Use fallback
        if (_fallbackMaterial != null)
        {
            if (_debugMode)
                Debug.Log("WallMaterialHelper: Using fallback material");
            
            return _fallbackMaterial;
        }
        
        // Create fallback as last resort
        CreateFallbackMaterial();
        return _fallbackMaterial;
    }
    
    /// <summary>
    /// Create a fallback material for walls
    /// </summary>
    private void CreateFallbackMaterial()
    {
        _fallbackMaterial = new Material(Shader.Find("Unlit/Color"));
        
        // If Unlit/Color is not available, try Standard
        if (_fallbackMaterial.shader == null)
        {
            Debug.LogWarning("WallMaterialHelper: Unlit/Color shader not found, trying Standard shader");
            _fallbackMaterial = new Material(Shader.Find("Standard"));
        }
        
        // Set a default color
        _fallbackMaterial.color = new Color(0.7f, 0.7f, 0.7f, 0.7f);
        
        // Setup for transparency
        _fallbackMaterial.SetFloat("_Mode", 3); // Transparent mode
        _fallbackMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _fallbackMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _fallbackMaterial.SetInt("_ZWrite", 0);
        _fallbackMaterial.DisableKeyword("_ALPHATEST_ON");
        _fallbackMaterial.EnableKeyword("_ALPHABLEND_ON");
        _fallbackMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _fallbackMaterial.renderQueue = 3000;
        
        if (_debugMode)
            Debug.Log("WallMaterialHelper: Created fallback material");
    }
    
    /// <summary>
    /// Fix all wall materials in the scene
    /// </summary>
    public void FixWallMaterials()
    {
        // Find all objects that might need fixing
        ARWallPainter[] wallPainters = FindObjectsOfType<ARWallPainter>();
        WallMeshRenderer[] wallRenderers = FindObjectsOfType<WallMeshRenderer>();
        WallMeshMaterialFixer[] materialFixers = FindObjectsOfType<WallMeshMaterialFixer>();
        
        // Get the correct material
        Material correctMaterial = GetWallMaterial();
        
        // Fix ARWallPainter components
        foreach (ARWallPainter painter in wallPainters)
        {
            if (painter._wallMaterial == null || painter._wallMaterial.shader == null)
            {
                painter._wallMaterial = correctMaterial;
                
                if (_debugMode)
                    Debug.Log($"WallMaterialHelper: Fixed material for {painter.name}");
            }
        }
        
        // Fix WallMeshMaterialFixer components
        foreach (WallMeshMaterialFixer fixer in materialFixers)
        {
            if (fixer.wallMeshMaterial == null || fixer.wallMeshMaterial.shader == null)
            {
                fixer.wallMeshMaterial = correctMaterial;
                
                if (_debugMode)
                    Debug.Log($"WallMaterialHelper: Fixed material for {fixer.name}");
            }
        }
        
        // Log summary
        if (_debugMode)
            Debug.Log($"WallMaterialHelper: Fixed materials for {wallPainters.Length} wall painters, {materialFixers.Length} material fixers");
    }
} 