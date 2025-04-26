using UnityEngine;
using Unity.Barracuda;
using System.Linq;
using System.Collections;
using ML; // Add ML namespace for SegmentationManager
using UnityEngine.SceneManagement;
using ML.DeepLab;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Utility component to check and fix model configuration at runtime or in editor
/// </summary>
public class ModelConfigFixerComponent : MonoBehaviour
{
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private EnhancedDeepLabPredictor enhancedPredictor;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private NNModel modelAsset;
    
    [ContextMenu("Fix Model Config")]
    public void FixModelConfig()
    {
        if (modelAsset == null)
        {
            Debug.LogError("Model asset is not assigned!");
            return;
        }

        if (segmentationManager != null)
        {
            var model = ModelLoader.Load(modelAsset);
            
            // Log the model info
            Debug.Log($"Model inputs: {model.inputs.Count}, outputs: {model.outputs.Count}");
            
            // Set model asset using the property (now public)
            segmentationManager.ModelAsset = modelAsset;
            Debug.Log("Model asset set for SegmentationManager");
            
            // Initialize model (now public method)
            segmentationManager.InitializeModel();
            Debug.Log("Model initialized for SegmentationManager");
        }
        
        if (enhancedPredictor != null)
        {
            // Use reflection to set the property if directly accessing it doesn't work
            var modelAssetProp = enhancedPredictor.GetType().GetProperty("modelAsset");
            if (modelAssetProp != null && modelAssetProp.CanWrite)
            {
                modelAssetProp.SetValue(enhancedPredictor, modelAsset);
            }
            else
            {
                var field = enhancedPredictor.GetType().GetField("modelAsset", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(enhancedPredictor, modelAsset);
                }
            }
            Debug.Log("Model asset set for EnhancedDeepLabPredictor");
        }
        
        if (deepLabPredictor != null)
        {
            // Use reflection to set the property if directly accessing it doesn't work
            var modelAssetProp = deepLabPredictor.GetType().GetProperty("modelAsset");
            if (modelAssetProp != null && modelAssetProp.CanWrite)
            {
                modelAssetProp.SetValue(deepLabPredictor, modelAsset);
            }
            else
            {
                var field = deepLabPredictor.GetType().GetField("modelAsset", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(deepLabPredictor, modelAsset);
                }
            }
            Debug.Log("Model asset set for DeepLabPredictor");
        }
    }
    
    [ContextMenu("Analyze Model")]
    public void AnalyzeModel()
    {
        if (modelAsset == null)
        {
            Debug.LogError("Model asset is not assigned!");
            return;
        }
        
        try
        {
            Model model = ModelLoader.Load(modelAsset);
            
            Debug.Log($"Model loaded: {modelAsset.name}");
            Debug.Log($"Inputs: {model.inputs.Count}, Outputs: {model.outputs.Count}");
            
            // Log inputs
            Debug.Log("Model inputs:");
            foreach (var input in model.inputs)
            {
                Debug.Log($"- {input.name}");
            }
            
            // Log outputs
            Debug.Log("Model outputs:");
            foreach (var output in model.outputs)
            {
                Debug.Log($"- {output}");
            }
            
            // Try to analyze dimensions
            if (segmentationManager != null)
            {
                Debug.Log("Current SegmentationManager configuration:");
                Debug.Log($"Input dimensions: {segmentationManager.inputWidth}x{segmentationManager.inputHeight}x{segmentationManager.inputChannels}");
                Debug.Log($"Output name: {segmentationManager.outputName}");
                Debug.Log($"Wall class ID: {segmentationManager.wallClassId}");
            }
            
            // Create a placeholder tensor for inference shape analysis
            var dummyTensor = new Tensor(1, 224, 224, 3);
            Debug.Log($"Created dummy tensor with shape [1, 224, 224, 3]");
            
            dummyTensor.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error analyzing model: {e.Message}\n{e.StackTrace}");
        }
    }
} 