using UnityEngine;
using ML;
using ML.DeepLab;

/// <summary>
/// This script checks that the ML namespaces are properly defined
/// </summary>
public class MLNamespaceChecker : MonoBehaviour
{
    [SerializeField] private bool debugLog = true;
    
    private void Awake()
    {
        // Check ML namespace
        IMLModel testModel = null; // Just a reference to verify compilation
        MLComponent testComponent = null; // Just a reference to verify compilation
        ML.ModelType testType = ML.ModelType.Segmentation; // Just a reference to verify compilation
        
        // Check ML.DeepLab namespace
        DeepLabPredictor testPredictor = null; // Just a reference to verify compilation
        EnhancedDeepLabPredictor testEnhancedPredictor = null; // Just a reference to verify compilation
        
        if (debugLog)
        {
            Debug.Log("MLNamespaceChecker: Namespace verification successful!");
            Debug.Log($"ML.ModelType enum value: {testType}");
            Debug.Log("ML.DeepLab classes accessible");
        }
    }
    
    // Editor helper to check compile-time
#if UNITY_EDITOR
    [UnityEditor.MenuItem("ML/Verify Namespaces")]
    public static void VerifyNamespaces()
    {
        Debug.Log("ML Namespace verification successful! The namespaces are properly defined and can be used in the project.");
    }
#endif
} 