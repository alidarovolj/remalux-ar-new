using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils; // Added for XROrigin

/// <summary>
/// Управляет материалами стен и исправляет проблемы с ними
/// </summary>
[DefaultExecutionOrder(-100)] // Run early, before other components
public class WallMaterialFixManager : MonoBehaviour
{
    [Header("Materials")]
    public Material defaultWallMaterial;
    public Material fallbackWallMaterial;
    public Material transparentWallMaterial;
    
    [Header("Component References")]
    public XROrigin xrOrigin;
    public ARPlaneManager planeManager;
    public ARCameraManager cameraManager;
    
    // Legacy reference for backward compatibility
    [System.Obsolete("Use xrOrigin instead")]
    [HideInInspector]
    public ARSessionOrigin arSessionOrigin;
    
    [Header("Settings")]
    public bool fixOnAwake = true;
    public bool fixOnARSessionStart = true;
    public bool fixPeriodically = true;
    public float fixInterval = 2.0f;
    public bool debugMode = true;
    
    // Private members
    private float _lastFixTime;
    private bool _materialsInitialized = false;
    
    private void Awake()
    {
        // Migrate from ARSessionOrigin to XROrigin if needed
        if (xrOrigin == null && arSessionOrigin != null)
        {
            xrOrigin = arSessionOrigin.GetComponent<XROrigin>();
        }
        
        // Find references if not set
        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();
            
        if (planeManager == null && xrOrigin != null)
            planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            
        if (cameraManager == null && xrOrigin != null)
            cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
            
        // Create default materials if not set
        InitializeMaterials();
        
        // Fix materials on awake if requested
        if (fixOnAwake)
        {
            FixMaterials();
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to AR session state changed
        if (fixOnARSessionStart)
        {
            ARSession.stateChanged += OnARSessionStateChanged;
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from AR session state changed
        if (fixOnARSessionStart)
        {
            ARSession.stateChanged -= OnARSessionStateChanged;
        }
    }
    
    private void Update()
    {
        // Fix materials periodically if requested
        if (fixPeriodically && Time.time - _lastFixTime > fixInterval)
        {
            FixMaterials();
            _lastFixTime = Time.time;
        }
    }
    
    /// <summary>
    /// Initialize materials if they're not set
    /// </summary>
    private void InitializeMaterials()
    {
        if (_materialsInitialized)
            return;
            
        // Create default wall material if not set
        if (defaultWallMaterial == null)
        {
            // First try to find existing wall materials
            Material existingMaterial = Resources.Load<Material>("WallMaterial");
            if (existingMaterial != null)
            {
                defaultWallMaterial = existingMaterial;
            }
            else
            {
                // Create a new material with Unlit/Color shader
                defaultWallMaterial = new Material(Shader.Find("Unlit/Color"));
                
                // If Unlit/Color is not available, try Standard
                if (defaultWallMaterial.shader == null)
                {
                    defaultWallMaterial = new Material(Shader.Find("Standard"));
                }
                
                // Try custom shader
                if (defaultWallMaterial.shader == null)
                {
                    defaultWallMaterial = new Material(Shader.Find("Custom/SimpleUnlitColor"));
                }
                
                // Set default color
                defaultWallMaterial.color = new Color(0.7f, 0.7f, 0.7f, 0.7f);
                
                // Set rendering mode to transparent
                defaultWallMaterial.SetFloat("_Mode", 3); // Transparent mode
                defaultWallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                defaultWallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                defaultWallMaterial.SetInt("_ZWrite", 0);
                defaultWallMaterial.DisableKeyword("_ALPHATEST_ON");
                defaultWallMaterial.EnableKeyword("_ALPHABLEND_ON");
                defaultWallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                defaultWallMaterial.renderQueue = 3000;
                
                if (debugMode)
                    Debug.Log("WallMaterialFixManager: Created default wall material");
            }
        }
        
        // Create transparent wall material if not set
        if (transparentWallMaterial == null)
        {
            // Clone the default material
            transparentWallMaterial = Instantiate(defaultWallMaterial);
            transparentWallMaterial.name = "TransparentWallMaterial";
            
            // Set fully transparent color
            transparentWallMaterial.color = new Color(1f, 1f, 1f, 0f);
            
            if (debugMode)
                Debug.Log("WallMaterialFixManager: Created transparent wall material");
        }
        
        _materialsInitialized = true;
    }
    
    /// <summary>
    /// Fix all materials related to AR walls
    /// </summary>
    public void FixMaterials()
    {
        // Ensure materials are initialized
        InitializeMaterials();
        
        // Fix WallPainter materials
        FixWallPainterMaterials();
        
        // Fix AR plane materials
        FixARPlaneMaterials();
        
        if (debugMode)
            Debug.Log("WallMaterialFixManager: Fixed all materials");
    }
    
    /// <summary>
    /// Fix materials for all ARWallPainter components
    /// </summary>
    private void FixWallPainterMaterials()
    {
        ARWallPainter[] wallPainters = FindObjectsOfType<ARWallPainter>();
        
        foreach (ARWallPainter painter in wallPainters)
        {
            // Fix wall material
            if (painter._wallMaterial == null || painter._wallMaterial.shader == null)
            {
                painter._wallMaterial = defaultWallMaterial;
                
                if (debugMode)
                    Debug.Log($"WallMaterialFixManager: Fixed material for {painter.name}");
            }
        }
    }
    
    /// <summary>
    /// Fix materials for AR planes
    /// </summary>
    private void FixARPlaneMaterials()
    {
        if (planeManager == null)
            return;
            
        // Fix plane prefab material
        if (planeManager.planePrefab != null)
        {
            MeshRenderer prefabRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (prefabRenderer != null && (prefabRenderer.sharedMaterial == null || prefabRenderer.sharedMaterial.shader == null))
            {
                prefabRenderer.sharedMaterial = defaultWallMaterial;
                
                if (debugMode)
                    Debug.Log("WallMaterialFixManager: Fixed plane prefab material");
            }
        }
        
        // Fix existing planes
        foreach (ARPlane plane in planeManager.trackables)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null && (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null))
            {
                renderer.sharedMaterial = defaultWallMaterial;
                
                if (debugMode)
                    Debug.Log($"WallMaterialFixManager: Fixed material for plane {plane.trackableId}");
            }
        }
    }
    
    /// <summary>
    /// Handle AR session state changes
    /// </summary>
    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        if (args.state == ARSessionState.SessionTracking)
        {
            // AR session is fully initialized, fix materials again
            FixMaterials();
            
            if (debugMode)
                Debug.Log("WallMaterialFixManager: Fixed materials after AR session tracking");
        }
    }
} 