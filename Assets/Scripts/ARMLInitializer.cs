using UnityEngine;
using System.Collections;
using ML;
using ML.DeepLab;
using System.Reflection;
using UnityEngine.XR.ARFoundation;

// Используем разные пространства имен в зависимости от наличия пакетов
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Initializes and connects AR and ML components at runtime
/// </summary>
public class ARMLInitializer : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private bool initializeOnAwake = true;
    [SerializeField] private bool initializeOnStart = true;
    [SerializeField] private bool verbose = true;
    
    [Header("References")]
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private EnhancedDeepLabPredictor deepLabPredictor;
    [SerializeField] private MLManagerAdapter mlManagerAdapter;
    [SerializeField] private ARCameraManager arCameraManager;
    
    private bool isInitialized = false;
    
    private void Awake()
    {
        if (initializeOnAwake)
        {
            Initialize();
        }
    }
    
    private void Start()
    {
        if (initializeOnStart && !isInitialized)
        {
            Initialize();
        }
    }
    
    /// <summary>
    /// Initialize and connect AR and ML components
    /// </summary>
    public void Initialize()
    {
        LogMessage("Initializing AR ML Connector...");
        
        // Find components if not assigned
        FindComponents();
        
        // Setup components
        SetupSegmentationManager();
        SetupDeepLabPredictor();
        SetupARMLConnection();
        
        isInitialized = true;
        LogMessage("AR ML initialization complete");
    }
    
    /// <summary>
    /// Find and assign components if not already set
    /// </summary>
    private void FindComponents()
    {
        LogMessage("Finding components...");
        
        // Find SegmentationManager
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager != null)
            {
                LogMessage("Found SegmentationManager automatically");
            }
            else
            {
                Debug.LogWarning("SegmentationManager not found. ML functionality may be limited.");
            }
        }
        
        // Find EnhancedDeepLabPredictor
        if (deepLabPredictor == null)
        {
            deepLabPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            if (deepLabPredictor != null)
            {
                LogMessage("Found EnhancedDeepLabPredictor automatically");
            }
            else
            {
                Debug.LogWarning("EnhancedDeepLabPredictor not found. ML functionality may be limited.");
            }
        }
        
        // Find MLManagerAdapter
        if (mlManagerAdapter == null)
        {
            mlManagerAdapter = FindObjectOfType<MLManagerAdapter>();
            if (mlManagerAdapter != null)
            {
                LogMessage("Found MLManagerAdapter automatically");
            }
            else
            {
                Debug.LogWarning("MLManagerAdapter not found. AR-ML connection may not work properly.");
            }
        }
        
        // Find ARCameraManager
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager != null)
            {
                LogMessage("Found ARCameraManager automatically");
            }
            else
            {
                Debug.LogWarning("ARCameraManager not found. AR camera feed may not be available.");
            }
        }
    }
    
    /// <summary>
    /// Setup SegmentationManager component
    /// </summary>
    private void SetupSegmentationManager()
    {
        if (segmentationManager == null) return;
        
        LogMessage("Setting up SegmentationManager...");
        
        // Check for ModelConfigFixer
        var modelFixer = segmentationManager.GetComponent<ModelConfigFixer>();
        if (modelFixer == null)
        {
            LogMessage("Adding ModelConfigFixer to SegmentationManager");
            modelFixer = segmentationManager.gameObject.AddComponent<ModelConfigFixer>();
            
            // Set the segmentationManager reference
            var segManagerField = modelFixer.GetType().GetField("segmentationManager", 
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                
            if (segManagerField != null)
            {
                segManagerField.SetValue(modelFixer, segmentationManager);
            }
            else
            {
                Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
            }
        }
        
        // Apply fixes - make this configurable 
        // This needs the public FixModelConfiguration method
        try
        {
            var fixMethod = modelFixer.GetType().GetMethod("FixModelConfiguration", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (fixMethod != null)
            {
                fixMethod.Invoke(modelFixer, null);
                LogMessage("Applied model configuration fixes");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error applying model fixes: {e.Message}");
        }
    }
    
    /// <summary>
    /// Setup EnhancedDeepLabPredictor component
    /// </summary>
    private void SetupDeepLabPredictor()
    {
        if (deepLabPredictor == null) return;
        
        LogMessage("Setting up EnhancedDeepLabPredictor...");
        
        // Configure settings for optimal performance and quality
        // Set properties via reflection where needed
        SetFieldByNameOrProperty(deepLabPredictor, "WallClassId", 9);
        SetFieldByNameOrProperty(deepLabPredictor, "enableDownsampling", true);
        SetFieldByNameOrProperty(deepLabPredictor, "downsamplingFactor", 2);
        SetFieldByNameOrProperty(deepLabPredictor, "minSegmentationInterval", 0.2f);
        SetFieldByNameOrProperty(deepLabPredictor, "applyNoiseReduction", true);
        SetFieldByNameOrProperty(deepLabPredictor, "applyWallFilling", true);
        
        LogMessage("EnhancedDeepLabPredictor configuration complete");
    }
    
    /// <summary>
    /// Setup connections between AR and ML components
    /// </summary>
    private void SetupARMLConnection()
    {
        if (mlManagerAdapter == null) return;
        
        LogMessage("Setting up AR-ML connection...");
        
        // Connect to SegmentationManager
        if (segmentationManager != null)
        {
            SetFieldByNameOrProperty(mlManagerAdapter, "segmentationManager", segmentationManager);
            LogMessage("Connected MLManagerAdapter to SegmentationManager");
        }
        
        // Connect to ARCameraManager
        if (arCameraManager != null)
        {
            SetFieldByNameOrProperty(mlManagerAdapter, "cameraManager", arCameraManager);
            LogMessage("Connected MLManagerAdapter to ARCameraManager");
        }
        
        // Set processing interval
        SetFieldByNameOrProperty(mlManagerAdapter, "processingInterval", 0.5f);
        
        LogMessage("AR-ML connection setup complete");
    }
    
    /// <summary>
    /// Set a field or property value via reflection
    /// </summary>
    private void SetFieldByNameOrProperty(object target, string fieldName, object value)
    {
        if (target == null)
        {
            Debug.LogError($"Cannot set field {fieldName} on null target");
            return;
        }
        
        // Try normal property
        PropertyInfo property = target.GetType().GetProperty(fieldName, 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return;
        }
        
        // Try public field
        FieldInfo publicField = target.GetType().GetField(fieldName, 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (publicField != null)
        {
            publicField.SetValue(target, value);
            return;
        }
        
        // Try private field
        FieldInfo privateField = target.GetType().GetField(fieldName, 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (privateField != null)
        {
            privateField.SetValue(target, value);
            return;
        }
        
        Debug.LogWarning($"Could not find any way to set {fieldName} on {target.GetType().Name}");
    }
    
    /// <summary>
    /// Log a message with appropriate verbosity
    /// </summary>
    private void LogMessage(string message)
    {
        if (verbose)
        {
            Debug.Log($"[ARMLInitializer] {message}");
        }
    }
} 