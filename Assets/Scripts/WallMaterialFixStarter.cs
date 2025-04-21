using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Автоматически запускает WallMaterialHelper при старте сцены
/// </summary>
public class WallMaterialFixStarter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Material _defaultWallMaterial;
    [SerializeField] private Material _fallbackWallMaterial;
    
    [Header("Settings")]
    [SerializeField] private bool _fixOnStart = true;
    [SerializeField] private bool _fixAfterARSessionStart = true;
    [SerializeField] private bool _debugMode = true;
    
    // AR components 
    private ARSession _arSession;
    
    private void Awake()
    {
        // Find AR session
        _arSession = FindObjectOfType<ARSession>();
    }
    
    private void Start()
    {
        if (_fixOnStart)
        {
            // Create WallMaterialHelper if it doesn't exist
            WallMaterialHelper helper = WallMaterialHelper.Instance;
            
            // Configure it from our settings
            ConfigureHelper(helper);
            
            // Fix materials immediately
            helper.FixWallMaterials();
            
            if (_debugMode)
                Debug.Log("WallMaterialFixStarter: Fixed materials on start");
        }
        
        // Subscribe to AR session state change if needed
        if (_fixAfterARSessionStart && _arSession != null)
        {
            ARSession.stateChanged += OnARSessionStateChanged;
            
            if (_debugMode)
                Debug.Log("WallMaterialFixStarter: Subscribed to AR session state changes");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from AR session state change
        if (_fixAfterARSessionStart && _arSession != null)
        {
            ARSession.stateChanged -= OnARSessionStateChanged;
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
            WallMaterialHelper helper = WallMaterialHelper.Instance;
            helper.FixWallMaterials();
            
            if (_debugMode)
                Debug.Log("WallMaterialFixStarter: Fixed materials after AR session tracking");
            
            // Unsubscribe since we only need to do this once
            ARSession.stateChanged -= OnARSessionStateChanged;
        }
    }
    
    /// <summary>
    /// Configure the WallMaterialHelper with our settings
    /// </summary>
    private void ConfigureHelper(WallMaterialHelper helper)
    {
        // Set materials via reflection (since they're private)
        System.Reflection.FieldInfo wallMaterialField = typeof(WallMaterialHelper).GetField("_wallMaterial", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        System.Reflection.FieldInfo fallbackMaterialField = typeof(WallMaterialHelper).GetField("_fallbackMaterial", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        System.Reflection.FieldInfo debugModeField = typeof(WallMaterialHelper).GetField("_debugMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (wallMaterialField != null && _defaultWallMaterial != null)
        {
            wallMaterialField.SetValue(helper, _defaultWallMaterial);
        }
        
        if (fallbackMaterialField != null && _fallbackWallMaterial != null)
        {
            fallbackMaterialField.SetValue(helper, _fallbackWallMaterial);
        }
        
        if (debugModeField != null)
        {
            debugModeField.SetValue(helper, _debugMode);
        }
    }
    
    /// <summary>
    /// Fix wall materials manually (can be called from a button)
    /// </summary>
    public void FixWallMaterials()
    {
        WallMaterialHelper.Instance.FixWallMaterials();
        
        if (_debugMode)
            Debug.Log("WallMaterialFixStarter: Fixed materials manually");
    }
} 