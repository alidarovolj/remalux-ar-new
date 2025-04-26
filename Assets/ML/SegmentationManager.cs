using UnityEngine;
using Unity.Barracuda;
using System.Threading;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using System.IO;
#endif

/// <summary>
/// Manages ML model inference for wall segmentation.
/// Handles loading, running the model, and providing raw segmentation output.
/// </summary>
public class SegmentationManager : MonoBehaviour
{
    [Header("Model Settings")]
    [Tooltip("Assign the model.onnx asset here. This is the only supported model for the AR scene.")]
    [SerializeField] private NNModel modelAsset;
    [SerializeField] private string inputName = "ImageTensor";
    [SerializeField] private string outputName = "SemanticPredictions";
    [SerializeField] private int inputWidth = 224;
    [SerializeField] private int inputHeight = 224;
    
    [Header("Segmentation Settings")]
    [SerializeField] private byte wallClassId = 9; // ADE20K wall class ID
    [SerializeField] private bool useComputeOptimization = true;
    [SerializeField] private int processingInterval = 5; // Process every N frames
    [SerializeField] private bool modelOutputNeedsArgMax = false; // Set true if model outputs logits/probabilities
    
    [Header("Advanced Settings")]
    [SerializeField] private WorkerFactory.Type workerType = WorkerFactory.Type.ComputePrecompiled;
    [Tooltip("Set 0 for automatic device selection")]
    [SerializeField] private int computeDeviceIndex = 0;
    [Tooltip("Set false if your model uses NCHW (channels first) format")]
    [SerializeField] private bool isModelNHWCFormat = true; // Most TensorFlow/ONNX models use NHWC format
    
    // ML model and engine
    private Model _runtimeModel;
    private IWorker _engine;
    private int _frameCounter = 0;
    private bool _isProcessing = false;
    
    // Pre-allocated buffers for efficiency
    private Texture2D _resizedInput;
    private Texture2D _outputTexture;
    
    // Events
    public event Action<byte, Texture2D> OnSegmentationCompleted;
    
    // Properties
    public byte WallClassId => wallClassId;
    public bool IsProcessing => _isProcessing;
    
    private void Awake()
    {
        // Load and prepare model
        InitializeModel();
        
        // Pre-allocate textures
        _resizedInput = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
        _outputTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.R8, false);
    }
    
    private void InitializeModel()
    {
        if (modelAsset == null)
        {
            Debug.LogError("No model asset assigned to SegmentationManager!");
            enabled = false;
            return;
        }
        
        // Verify this is the correct model (model.onnx)
        if (!modelAsset.name.Contains("model"))
        {
            Debug.LogWarning($"SegmentationManager: The model '{modelAsset.name}' is being used, but 'model.onnx' is the only fully supported model for this project. This may cause issues with wall detection and segmentation. Please update references to use model.onnx.");
        }
        
        // Load model
        _runtimeModel = ModelLoader.Load(modelAsset);
        
        // Check for valid input/output names
        bool validInputs = false;
        bool validOutputs = false;
        
        // Log all available inputs/outputs
        Debug.Log($"Model inputs: {string.Join(", ", GetInputLayerNames(_runtimeModel))}");
        Debug.Log($"Model outputs: {string.Join(", ", _runtimeModel.outputs)}");
        
        // Exact match for input name
        foreach (var input in _runtimeModel.inputs)
        {
            if (input.name.Equals(inputName, StringComparison.OrdinalIgnoreCase) || 
                input.name.Contains(inputName))
            {
                inputName = input.name; // Use actual name from model
                validInputs = true;
                Debug.Log($"Using input layer: {inputName}");
                break;
            }
        }
        
        // If exact match not found, try partial match but log a warning
        if (!validInputs)
        {
            foreach (var input in _runtimeModel.inputs)
            {
                if (input.name.Contains(inputName))
                {
                    inputName = input.name;
                    validInputs = true;
                    Debug.LogWarning($"Exact input layer name not found. Using closest match: {inputName}");
                    break;
                }
            }
            
            // If still not found, use the first input as fallback
            if (!validInputs && _runtimeModel.inputs.Count > 0)
            {
                inputName = _runtimeModel.inputs[0].name;
                validInputs = true;
                Debug.LogWarning($"Input layer '{inputName}' not found. Using first available input: {inputName}");
            }
        }
        
        // Check input shape to auto-detect tensor format
        foreach (var input in _runtimeModel.inputs)
        {
            if (input.name == inputName)
            {
                // Check tensor shape if available
                if (input.shape[3] == 3 && input.shape.Length == 4)
                {
                    // Try to detect NCHW vs NHWC format
                    // In NCHW: [batch, channels, height, width]
                    // In NHWC: [batch, height, width, channels]
                    // Note: This is a heuristic and might not be 100% accurate
                    if (input.shape[1] == 3)
                    {
                        isModelNHWCFormat = false; // Likely NCHW format (channels as dimension 1)
                        Debug.Log("Detected NCHW tensor format based on input shape");
                    }
                    else if (input.shape[3] == 3)
                    {
                        isModelNHWCFormat = true; // Likely NHWC format (channels as dimension 3)
                        Debug.Log("Detected NHWC tensor format based on input shape");
                    }
                }
                break;
            }
        }
        
        // Exact match for output name
        foreach (var output in _runtimeModel.outputs)
        {
            if (output.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            {
                outputName = output; // Use actual name from model
                validOutputs = true;
                Debug.Log($"Using output layer: {outputName}");
                break;
            }
        }
        
        // If exact match not found, try partial match or standard names
        if (!validOutputs)
        {
            // Common output layer names for segmentation models
            string[] possibleOutputNames = new string[] 
            {
                "SemanticPredictions", "ArgMax", "final_output", "predictions", 
                "softmax", "logits", "output"
            };
            
            // First try partial match with original name
            foreach (var output in _runtimeModel.outputs)
            {
                if (output.Contains(outputName))
                {
                    outputName = output;
                    validOutputs = true;
                    Debug.LogWarning($"Exact output layer name not found. Using closest match: {outputName}");
                    break;
                }
            }
            
            // If still not found, try common output names
            if (!validOutputs)
            {
                foreach (var possibleName in possibleOutputNames)
                {
                    foreach (var output in _runtimeModel.outputs)
                    {
                        if (output.Contains(possibleName))
                        {
                            outputName = output;
                            validOutputs = true;
                            Debug.LogWarning($"Output layer '{outputName}' not found. Using common name match: {outputName}");
                            
                            // If we found logits/softmax, we likely need to perform argmax
                            if (possibleName == "logits" || possibleName == "softmax")
                            {
                                modelOutputNeedsArgMax = true;
                                Debug.LogWarning("Found logits/softmax output - enabling ArgMax processing");
                            }
                            
                            break;
                        }
                    }
                    
                    if (validOutputs) break;
                }
            }
            
            // Final fallback to first output
            if (!validOutputs && _runtimeModel.outputs.Count > 0)
            {
                outputName = _runtimeModel.outputs[0];
                validOutputs = true;
                Debug.LogWarning($"No matching output layer found. Using first available output: {outputName}");
            }
        }
        
        if (!validInputs || !validOutputs)
        {
            Debug.LogError($"Model doesn't contain expected input/output layers. Disabling segmentation.");
            enabled = false;
            return;
        }
        
        // Select appropriate backend
        WorkerFactory.Type backend = workerType;
        
        // For mobile optimization
        if (Application.isMobilePlatform && useComputeOptimization)
        {
            backend = WorkerFactory.ValidateType(WorkerFactory.Type.ComputePrecompiled);
            
            // Fallback if compute not supported
            if (backend != WorkerFactory.Type.ComputePrecompiled)
                backend = WorkerFactory.Type.CSharpBurst;
        }
        
        // Create ML worker with selected backend
        try
        {
            // Create worker with selected backend
            // For specific compute device selection
            if (computeDeviceIndex > 0) 
            {
                // Use the correct method signature: CreateWorker(Type, Model, bool)
                _engine = WorkerFactory.CreateWorker(backend, _runtimeModel, false);
                Debug.Log($"SegmentationManager: Created ML engine using {backend} backend with device index {computeDeviceIndex}");
            }
            else
            {
                _engine = WorkerFactory.CreateWorker(backend, _runtimeModel);
                Debug.Log($"SegmentationManager: Created ML engine using {backend} backend");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create ML worker: {e.Message}. Falling back to CPU.");
            _engine = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, _runtimeModel);
        }
    }
    
    private IEnumerable<string> GetInputLayerNames(Model model)
    {
        foreach (var input in model.inputs)
        {
            yield return input.name;
        }
    }
    
    /// <summary>
    /// Process a camera frame through the segmentation model
    /// </summary>
    public void ProcessCameraFrame(Texture2D cameraFrame)
    {
        _frameCounter++;
        
        // Skip processing based on interval for performance
        if (_isProcessing || _frameCounter % processingInterval != 0)
            return;
            
        _isProcessing = true;
        
        try
        {
            // Resize input
            ResizeTexture(cameraFrame, _resizedInput);
            
            // Create input tensor (normalized 0-1)
            using (var inputTensor = new Tensor(_resizedInput, channels: 3))
            {
                // Execute model
                _engine.Execute(inputTensor);
                
                // Get output
                var outputTensor = _engine.PeekOutput(outputName);
                
                // Convert output tensor to texture
                ConvertTensorToTexture(outputTensor, _outputTexture);
                
                // Notify listeners
                OnSegmentationCompleted?.Invoke(wallClassId, _outputTexture);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during segmentation: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            _isProcessing = false;
        }
    }
    
    /// <summary>
    /// Resize source texture to target dimensions
    /// </summary>
    private void ResizeTexture(Texture2D source, Texture2D destination)
    {
        // Use bilinear scaling via RenderTexture for efficiency
        RenderTexture rt = RenderTexture.GetTemporary(
            destination.width, 
            destination.height, 
            0, 
            RenderTextureFormat.ARGB32
        );
        
        Graphics.Blit(source, rt);
        
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        destination.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
        destination.Apply();
        
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);
    }
    
    /// <summary>
    /// Convert tensor output to single-channel texture with wall class
    /// </summary>
    private void ConvertTensorToTexture(Tensor tensor, Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        
        // Get raw data for direct processing
        float[] data = tensor.AsFloats();
        Color32[] pixels = new Color32[width * height];
        
        // Different processing depending on whether the model gives class indices directly
        // or we need to perform argmax ourselves
        if (modelOutputNeedsArgMax)
        {
            // Model outputs class probabilities, we need to find the highest value (argmax)
            int numClasses = tensor.shape[3];
            
            // Process each pixel to find wall class
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // Perform argmax across the channels (classes) dimension
                    int bestClassId = 0;
                    float bestClassConfidence = -1f;
                    
                    // Find class with highest probability
                    for (int c = 0; c < numClasses; c++)
                    {
                        int index = GetTensorIndex(x, y, c, width, height, numClasses);
                        float probability = data[index];
                        
                        if (probability > bestClassConfidence)
                        {
                            bestClassConfidence = probability;
                            bestClassId = c;
                        }
                    }
                    
                    // Check if this pixel is a wall (best class is wall class)
                    byte value = bestClassId == wallClassId ? (byte)255 : (byte)0;
                    
                    // Set all channels for compatibility
                    pixels[pixelIndex] = new Color32(value, value, value, 255);
                }
            }
        }
        else
        {
            // Model already outputs class indices directly
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // Expected format: [batch, width, height, 1] where value is the class ID
                    float classId = data[pixelIndex];
                    
                    // Check if this pixel is a wall
                    byte value = Mathf.RoundToInt(classId) == wallClassId ? (byte)255 : (byte)0;
                    
                    // Set all channels for compatibility
                    pixels[pixelIndex] = new Color32(value, value, value, 255);
                }
            }
        }
        
        // Apply to texture
        texture.SetPixels32(pixels);
        texture.Apply();
    }
    
    /// <summary>
    /// Calculate tensor index for a specific pixel and channel
    /// </summary>
    private int GetTensorIndex(int x, int y, int channel, int width, int height, int numChannels)
    {
        // Different models might have different memory layouts
        if (isModelNHWCFormat)
        {
            // NHWC layout (batch, height, width, channel) - most common in TensorFlow/ONNX
            return (y * width * numChannels) + (x * numChannels) + channel;
        }
        else
        {
            // NCHW layout (batch, channel, height, width) - common in PyTorch/Caffe
            return (channel * height * width) + (y * width) + x;
        }
    }
    
    private void OnDestroy()
    {
        try
        {
            // Clean up Barracuda resources
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
                Debug.Log("SegmentationManager: Disposed ML engine");
            }
            
            // Clean up textures
            if (_resizedInput != null)
            {
                Destroy(_resizedInput);
                _resizedInput = null;
            }
                
            if (_outputTexture != null)
            {
                Destroy(_outputTexture);
                _outputTexture = null;
            }
            
            Debug.Log("SegmentationManager: Cleaned up all resources");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during SegmentationManager cleanup: {e.Message}");
        }
    }
    
#if UNITY_EDITOR
    [ContextMenu("Debug Save Sample Output")]
    public void DebugSaveOutput()
    {
        if (_outputTexture != null)
        {
            byte[] bytes = _outputTexture.EncodeToPNG();
            File.WriteAllBytes(Application.dataPath + "/debug_segmentation.png", bytes);
            Debug.Log("Saved debug output to Assets/debug_segmentation.png");
        }
    }
    
    [ContextMenu("Log Model Layer Info")]
    public void LogModelLayerInfo()
    {
        if (_runtimeModel != null)
        {
            Debug.Log("MODEL INPUT LAYERS:");
            foreach (var input in _runtimeModel.inputs)
            {
                Debug.Log($"  - {input.name} (Shape: {input.shape})");
            }
            
            Debug.Log("MODEL OUTPUT LAYERS:");
            foreach (var output in _runtimeModel.outputs)
            {
                Debug.Log($"  - {output}");
            }
        }
        else
        {
            Debug.LogWarning("No model loaded yet.");
        }
    }
    
    [ContextMenu("Test Tensor Format")]
    public void TestTensorFormat()
    {
        if (_runtimeModel != null)
        {
            foreach (var input in _runtimeModel.inputs)
            {
                if (input.shape[3] == 3 && input.shape.Length == 4)
                {
                    string format = "Unknown";
                    if (input.shape[1] == 3)
                        format = "NCHW (Channels first)";
                    else if (input.shape[3] == 3)
                        format = "NHWC (Channels last)";
                    
                    Debug.Log($"Input {input.name} with shape {input.shape} likely uses {format} format");
                }
            }
        }
    }
#endif
} 