using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Reflection;
using ML;
using ML.DeepLab;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class EditorModelConfigFixer : MonoBehaviour
{
    [SerializeField, HideInInspector] private SegmentationManager segmentationManager;
    
    public void FixModelConfiguration()
    {
        Debug.Log("Applying model configuration fixes...");
        
        // Fix SegmentationManager settings
        FixSegmentationManager();
        
        // Fix MLManagerAdapter if it exists
        FixMLManagerAdapter();
        
        // Fix DeepLabPredictor if it exists
        FixDeepLabPredictor();
        
        Debug.Log("Model configuration fixes applied successfully!");
    }
    
    private void FixSegmentationManager()
    {
        if (segmentationManager == null)
        {
            segmentationManager = GetComponent<SegmentationManager>();
            if (segmentationManager == null)
            {
                Debug.LogError("SegmentationManager component not found");
                return;
            }
        }
        
        // Use reflection to set private fields
        SetPrivateField(segmentationManager, "_outputName", "logits");
        SetPrivateField(segmentationManager, "_wallClassId", 9);
        SetPrivateField(segmentationManager, "_inputWidth", 224);
        SetPrivateField(segmentationManager, "_inputHeight", 224);
        SetPrivateField(segmentationManager, "_inputChannels", 3);
        SetPrivateField(segmentationManager, "_debugMode", true);
        
        Debug.Log("Fixed SegmentationManager configuration");
    }
    
    private void FixMLManagerAdapter()
    {
        var adapter = FindObjectOfType<MLManagerAdapter>();
        if (adapter != null)
        {
            // Use reflection to set private fields
            SetPrivateField(adapter, "_processingInterval", 0.5f);
            SetPrivateField(adapter, "_downsampleFactor", 4);
            
            Debug.Log("Fixed MLManagerAdapter configuration");
        }
    }
    
    private void FixDeepLabPredictor()
    {
        var predictor = FindObjectOfType<DeepLabPredictor>();
        if (predictor != null)
        {
            // Use reflection to set private fields if possible
            SetPrivateField(predictor, "_inputHeight", 224);
            SetPrivateField(predictor, "_inputWidth", 224);
            SetPrivateField(predictor, "_useStandardFormatInput", true);
            SetPrivateField(predictor, "_inputName", "ImageTensor");
            SetPrivateField(predictor, "_outputName", "SemanticPredictions");
            SetPrivateField(predictor, "_wallClassId", 9);
            
            Debug.Log("Fixed DeepLabPredictor configuration");
        }
        
        var enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        if (enhancedPredictor != null)
        {
            // Fix EnhancedDeepLabPredictor if it exists
            SetPrivateField(enhancedPredictor, "_inputHeight", 224);
            SetPrivateField(enhancedPredictor, "_inputWidth", 224);
            SetPrivateField(enhancedPredictor, "_useStandardFormatInput", true);
            
            Debug.Log("Fixed EnhancedDeepLabPredictor configuration");
        }
    }
    
    public void FixTensorShapeIssue()
    {
        if (segmentationManager == null)
        {
            segmentationManager = GetComponent<SegmentationManager>();
            if (segmentationManager == null)
            {
                Debug.LogError("SegmentationManager component not found");
                return;
            }
        }
        
        // First, try updating the model dimensions
        SetPrivateField(segmentationManager, "_inputWidth", 224);
        SetPrivateField(segmentationManager, "_inputHeight", 224);
        SetPrivateField(segmentationManager, "_inputChannels", 3);
        SetPrivateField(segmentationManager, "_outputName", "logits");
        
        // Force reinitialize the model
        var reinitializeMethod = segmentationManager.GetType().GetMethod("InitializeModel", 
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
        if (reinitializeMethod != null)
        {
            // First dispose of existing worker if any
            var disposeMethod = segmentationManager.GetType().GetMethod("DisposeWorker", 
                BindingFlags.Instance | BindingFlags.NonPublic);
                
            if (disposeMethod != null)
            {
                disposeMethod.Invoke(segmentationManager, null);
            }
            
            // Then reinitialize
            reinitializeMethod.Invoke(segmentationManager, null);
            Debug.Log("Reinitialized model with corrected dimensions");
        }
        
        // Try to log tensor dimensions for debugging
        var logDimensionsMethod = segmentationManager.GetType().GetMethod("LogTensorDimensions", 
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
        if (logDimensionsMethod != null)
        {
            logDimensionsMethod.Invoke(segmentationManager, null);
        }
        
        Debug.Log("Applied tensor shape fixes to SegmentationManager");
    }
    
    private void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null) return;
        
        var field = target.GetType().GetField(fieldName, 
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
        if (field != null)
        {
            field.SetValue(target, value);
            Debug.Log($"Set {fieldName} to {value} on {target.GetType().Name}");
        }
        else
        {
            Debug.LogWarning($"Field {fieldName} not found on {target.GetType().Name}");
        }
    }
    
    private T GetPrivateField<T>(object target, string fieldName)
    {
        if (target == null) return default;
        
        var field = target.GetType().GetField(fieldName, 
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
        if (field != null)
        {
            return (T)field.GetValue(target);
        }
        
        Debug.LogWarning($"Field {fieldName} not found on {target.GetType().Name}");
        return default;
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(EditorModelConfigFixer))]
    public class EditorModelConfigFixerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            EditorModelConfigFixer fixer = (EditorModelConfigFixer)target;
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Configuration Fixes", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Apply All Fixes"))
            {
                fixer.FixModelConfiguration();
            }
            
            if (GUILayout.Button("Fix Tensor Shape Issue"))
            {
                fixer.FixTensorShapeIssue();
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Component Fixes", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Fix SegmentationManager Only"))
            {
                fixer.FixSegmentationManager();
            }
            
            if (GUILayout.Button("Fix MLManagerAdapter Only"))
            {
                fixer.FixMLManagerAdapter();
            }
            
            if (GUILayout.Button("Fix DeepLabPredictor Only"))
            {
                fixer.FixDeepLabPredictor();
            }
        }
    }
#endif
} 