using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML;
using ML.DeepLab;
using System.Collections;
using UnityEditor;

/// <summary>
/// Helper script to automatically set up and fix AR with ML components
/// </summary>
public class SetupARWithML : MonoBehaviour
{
    [Header("Components")]
    public SegmentationManager segmentationManager;
    public MLManagerAdapter mlManagerAdapter;
    public ARSession arSession;
    public ARCameraManager arCameraManager;
    
    [Header("Settings")]
    public Unity.Barracuda.NNModel modelAsset;
    public bool fixOnAwake = true;
    public bool fixTensorIssues = true;
    public bool fixMemoryLeaks = true;
    public bool fixARSessions = true;
    
    [Header("Input Dimensions (For ML Model)")]
    public int inputWidth = 224;
    public int inputHeight = 224;
    public int inputChannels = 3;
    public string outputName = "logits";
    public bool isNHWCFormat = true;
    
    private void Awake()
    {
        if (fixOnAwake)
        {
            StartCoroutine(SetupWithDelay());
        }
    }
    
    private IEnumerator SetupWithDelay()
    {
        // Wait a frame to ensure all components are initialized
        yield return null;
        
        SetupAllComponents();
    }
    
    /// <summary>
    /// Sets up all AR and ML components with proper configuration
    /// </summary>
    [ContextMenu("Setup All Components")]
    public void SetupAllComponents()
    {
        if (fixARSessions)
        {
            FixARSessions();
        }
        
        FindMissingComponents();
        ConfigureSegmentationManager();
        ConfigureMLManagerAdapter();
        
        if (fixTensorIssues)
        {
            AddModelConfigFixer();
        }
        
        if (fixMemoryLeaks)
        {
            AddMemoryLeakFixer();
        }
        
        Debug.Log("SetupARWithML: All components have been set up and configured");
    }
    
    /// <summary>
    /// Finds and assigns missing component references
    /// </summary>
    private void FindMissingComponents()
    {
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager == null)
            {
                GameObject segObj = new GameObject("Segmentation Manager");
                segmentationManager = segObj.AddComponent<SegmentationManager>();
                Debug.Log("Created new SegmentationManager");
            }
        }
        
        if (mlManagerAdapter == null)
        {
            mlManagerAdapter = FindObjectOfType<MLManagerAdapter>();
            if (mlManagerAdapter == null)
            {
                GameObject mlObj = new GameObject("ML Manager Adapter");
                mlManagerAdapter = mlObj.AddComponent<MLManagerAdapter>();
                Debug.Log("Created new MLManagerAdapter");
            }
        }
        
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
            if (arSession == null)
            {
                GameObject sessionObj = new GameObject("AR Session");
                arSession = sessionObj.AddComponent<ARSession>();
                sessionObj.AddComponent<ARInputManager>();
                Debug.Log("Created new ARSession");
            }
        }
        
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
        }
    }
    
    /// <summary>
    /// Configures the SegmentationManager with proper settings
    /// </summary>
    private void ConfigureSegmentationManager()
    {
        if (segmentationManager == null) return;
        
        // Set the model asset
        if (modelAsset != null)
        {
            var modelAssetProperty = segmentationManager.GetType().GetProperty("ModelAsset");
            if (modelAssetProperty != null)
            {
                modelAssetProperty.SetValue(segmentationManager, modelAsset);
            }
            else
            {
                var modelAssetField = segmentationManager.GetType().GetField("ModelAsset", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (modelAssetField != null)
                {
                    modelAssetField.SetValue(segmentationManager, modelAsset);
                }
            }
        }
        
        // Set input dimensions
        segmentationManager.inputWidth = inputWidth;
        segmentationManager.inputHeight = inputHeight;
        segmentationManager.inputChannels = inputChannels;
        segmentationManager.outputName = outputName;
        segmentationManager.isModelNHWCFormat = isNHWCFormat;
        
        Debug.Log($"Configured SegmentationManager with input dimensions: {inputWidth}x{inputHeight}x{inputChannels}, " +
                  $"output name: {outputName}, NHWC format: {isNHWCFormat}");
    }
    
    /// <summary>
    /// Configures the MLManagerAdapter with the proper references
    /// </summary>
    private void ConfigureMLManagerAdapter()
    {
        if (mlManagerAdapter == null) return;
        
        // Set references
        var segManagerField = mlManagerAdapter.GetType().GetField("segmentationManager", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (segManagerField != null)
        {
            segManagerField.SetValue(mlManagerAdapter, segmentationManager);
        }
        
        var arCameraField = mlManagerAdapter.GetType().GetField("arCameraManager", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (arCameraField != null && arCameraManager != null)
        {
            arCameraField.SetValue(mlManagerAdapter, arCameraManager);
        }
        
        Debug.Log("Configured MLManagerAdapter with references to SegmentationManager and ARCameraManager");
    }
    
    /// <summary>
    /// Adds ModelConfigFixer to the SegmentationManager
    /// </summary>
    private void AddModelConfigFixer()
    {
        if (segmentationManager == null) return;
        
        ModelConfigFixer configFixer = segmentationManager.GetComponent<ModelConfigFixer>();
        if (configFixer == null)
        {
            configFixer = segmentationManager.gameObject.AddComponent<ModelConfigFixer>();
            configFixer.segmentationManager = segmentationManager;
            
            // Use reflection to set the fixOnAwake field since it might be private
            var fixOnAwakeField = configFixer.GetType().GetField("fixOnAwake", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Public);
            
            if (fixOnAwakeField != null)
            {
                fixOnAwakeField.SetValue(configFixer, true);
            }
            else
            {
                // Try to find a method to enable automatic fixing
                var enableAutoFixMethod = configFixer.GetType().GetMethod("EnableAutoFix", 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Public);
                
                if (enableAutoFixMethod != null)
                {
                    enableAutoFixMethod.Invoke(configFixer, null);
                }
                else
                {
                    // As a last resort, just call FixModelConfiguration directly
                    configFixer.FixModelConfiguration();
                }
            }
            
            Debug.Log("Added ModelConfigFixer to SegmentationManager");
        }
    }
    
    /// <summary>
    /// Adds ARSessionFixer to fix duplicate ARSessions
    /// </summary>
    private void FixARSessions()
    {
        GameObject fixerObj = GameObject.Find("ARSessionFixer");
        if (fixerObj == null)
        {
            fixerObj = new GameObject("ARSessionFixer");
            ARSessionFixer fixer = fixerObj.AddComponent<ARSessionFixer>();
            Debug.Log("Added ARSessionFixer to scene");
        }
    }
    
    /// <summary>
    /// Adds a component to fix memory leaks
    /// </summary>
    private void AddMemoryLeakFixer()
    {
        // Add SafeTextureReader
        if (FindObjectOfType<SafeTextureReader>() == null)
        {
            GameObject safeReaderObj = new GameObject("SafeTextureReader");
            safeReaderObj.AddComponent<SafeTextureReader>();
            Debug.Log("Added SafeTextureReader to scene");
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SetupARWithML))]
public class SetupARWithMLEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        SetupARWithML setup = (SetupARWithML)target;
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Setup All Components"))
        {
            setup.SetupAllComponents();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This component helps set up and configure AR with ML components.\n\n" +
            "- Fix ARSessions: Ensures only one ARSession is active\n" +
            "- Fix Tensor Issues: Adds ModelConfigFixer to handle tensor shape mismatches\n" +
            "- Fix Memory Leaks: Adds components to prevent tensor and texture memory leaks\n\n" +
            "Common working input dimensions: 224x224, 256x256, 513x513", 
            MessageType.Info);
    }
}
#endif
