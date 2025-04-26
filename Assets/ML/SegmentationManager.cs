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
    [SerializeField] private bool debugMode = false; // Добавляем отладочный режим
    
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
                if (input.shape.Length >= 4 && input.shape[3] == 3)
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
                // Get expected output shape for proper tensor handling
                var outputShape = DetermineOutputShape(_runtimeModel);
                
                if (debugMode && _frameCounter % 100 == 0)
                {
                    Debug.Log($"Model output shape: batch={outputShape.batch}, " +
                              $"height={outputShape.height}, width={outputShape.width}, " +
                              $"channels={outputShape.channels}");
                }
                
                // Execute model
                _engine.Execute(inputTensor);
                
                // Get output
                var outputTensor = _engine.PeekOutput(outputName);
                
                // Verify tensor dimensions
                if (_frameCounter % 100 == 0)
                {
                    Debug.Log($"Output tensor shape: {string.Join("x", outputTensor.shape)}");
                }
                
                // Convert output tensor to texture using dynamic shape info
                ConvertTensorToTexture(outputTensor, _outputTexture, outputShape);
                
                // Notify listeners
                OnSegmentationCompleted?.Invoke(wallClassId, _outputTexture);
            }
        }
        catch (Exception e)
        {
            // Provide more detailed diagnostics for reshape errors
            if (e.Message.Contains("reshape array"))
            {
                Debug.LogError($"Shape mismatch error in segmentation: {e.Message}");
                Debug.LogError($"Input texture dimensions: {_resizedInput.width}x{_resizedInput.height}");
                
                // Log expected model input/output shapes if possible
                try 
                {
                    var inputShape = _runtimeModel.inputs[0].shape;
                    Debug.LogError($"Model expects input shape: {string.Join("x", inputShape)}");
                    
                    // Provide suggestion
                    Debug.LogError("Try adjusting inputWidth/inputHeight to match model's expected dimensions " +
                                  "or ensure tensor format (NHWC/NCHW) is correctly set");
                }
                catch 
                {
                    // Fallback if we can't access shape info
                    Debug.LogError("Could not determine model's expected dimensions. Check model.onnx specifications.");
                }
            }
            else
            {
                Debug.LogError($"Error during segmentation: {e.Message}\n{e.StackTrace}");
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }
    
    /// <summary>
    /// Determines the expected output shape from the model
    /// </summary>
    private (int batch, int height, int width, int channels) DetermineOutputShape(Model model)
    {
        // Default fallback values
        int batch = 1;
        int height = inputHeight;
        int width = inputWidth;
        int channels = 1;
        
        try
        {
            // Get output shape from model metadata if available
            foreach (var output in model.outputs)
            {
                var shapeMap = model.GetShapeByName(output);
                
                if (output == outputName && shapeMap.HasValue)
                {
                    var shape = shapeMap.Value;
                    
                    // Interpret shape based on format
                    if (isModelNHWCFormat) // NHWC format [batch, height, width, channels]
                    {
                        if (shape.rank >= 4)
                        {
                            batch = (int)shape[0];
                            height = (int)shape[1];
                            width = (int)shape[2];
                            channels = (int)shape[3];
                        }
                    }
                    else // NCHW format [batch, channels, height, width]
                    {
                        if (shape.rank >= 4)
                        {
                            batch = (int)shape[0];
                            channels = (int)shape[1];
                            height = (int)shape[2];
                            width = (int)shape[3];
                        }
                    }
                    
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error determining output shape: {e.Message}. Using default values.");
        }
        
        return (batch, height, width, channels);
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
    private void ConvertTensorToTexture(Tensor tensor, Texture2D texture, (int batch, int height, int width, int channels) shape)
    {
        int textureWidth = texture.width;
        int textureHeight = texture.height;
        
        // Get raw data for direct processing
        float[] data = tensor.AsFloats();
        Color32[] pixels = new Color32[textureWidth * textureHeight];
        
        // Different processing depending on whether the model gives class indices directly
        // or we need to perform argmax ourselves
        if (modelOutputNeedsArgMax)
        {
            // Model outputs class probabilities, we need to find the highest value (argmax)
            int numClasses = shape.channels;
            
            // Process each pixel to find wall class
            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < textureWidth; x++)
                {
                    int pixelIndex = y * textureWidth + x;
                    
                    // Perform argmax across the channels (classes) dimension
                    int bestClassId = 0;
                    float bestClassConfidence = -1f;
                    
                    // Find class with highest probability
                    for (int c = 0; c < numClasses; c++)
                    {
                        // Use dynamic indexing based on the tensor's actual dimensions
                        int index = GetDynamicTensorIndex(x, y, c, tensor.shape, isModelNHWCFormat, textureWidth, textureHeight);
                        
                        if (index >= 0 && index < data.Length)
                        {
                            float probability = data[index];
                            
                            if (probability > bestClassConfidence)
                            {
                                bestClassConfidence = probability;
                                bestClassId = c;
                            }
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
            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < textureWidth; x++)
                {
                    int pixelIndex = y * textureWidth + x;
                    
                    // Get index dynamically based on tensor dimensions
                    int index = GetDirectTensorIndex(x, y, tensor.shape, isModelNHWCFormat, textureWidth, textureHeight);
                    
                    if (index >= 0 && index < data.Length)
                    {
                        // Expected format: [batch, width, height, 1] where value is the class ID
                        float classId = data[index];
                        
                        // Check if this pixel is a wall
                        byte value = Mathf.RoundToInt(classId) == wallClassId ? (byte)255 : (byte)0;
                        
                        // Set all channels for compatibility
                        pixels[pixelIndex] = new Color32(value, value, value, 255);
                    }
                    else
                    {
                        // Safety fallback for out-of-range indices
                        pixels[pixelIndex] = new Color32(0, 0, 0, 255);
                    }
                }
            }
        }
        
        // Apply to texture
        texture.SetPixels32(pixels);
        texture.Apply();
    }
    
    /// <summary>
    /// Get tensor index for argmax calculation, handling different tensor formats dynamically
    /// </summary>
    private int GetDynamicTensorIndex(int x, int y, int channel, TensorShape tensorShape, bool isNHWC, int textureWidth, int textureHeight)
    {
        // Safety check
        if (tensorShape.rank < 4)
        {
            Debug.LogWarning($"Unexpected tensor shape: {tensorShape}");
            return -1;
        }
        
        // Scale x and y to match tensor dimensions
        int tensorWidth = isNHWC ? tensorShape[2] : tensorShape[3];
        int tensorHeight = isNHWC ? tensorShape[1] : tensorShape[2];
        
        // Get tensor coordinates scaled to tensor dimensions
        int tx = Mathf.FloorToInt((float)x / textureWidth * tensorWidth);
        int ty = Mathf.FloorToInt((float)y / textureHeight * tensorHeight);
        
        // Clamp to valid range
        tx = Mathf.Clamp(tx, 0, tensorWidth - 1);
        ty = Mathf.Clamp(ty, 0, tensorHeight - 1);
        
        // Calculate index based on format
        if (isNHWC)
        {
            // NHWC format [batch, height, width, channel]
            int numChannels = tensorShape[3];
            return (ty * tensorWidth * numChannels) + (tx * numChannels) + channel;
        }
        else
        {
            // NCHW format [batch, channel, height, width]
            return (channel * tensorHeight * tensorWidth) + (ty * tensorWidth) + tx;
        }
    }
    
    /// <summary>
    /// Get tensor index for direct class index output
    /// </summary>
    private int GetDirectTensorIndex(int x, int y, TensorShape tensorShape, bool isNHWC, int textureWidth, int textureHeight)
    {
        // Safety check
        if (tensorShape.rank < 3)
        {
            Debug.LogWarning($"Unexpected tensor shape for direct index: {tensorShape}");
            return -1;
        }
        
        // Scale x and y to match tensor dimensions
        int tensorWidth, tensorHeight;
        
        if (isNHWC)
        {
            // NHWC format [batch, height, width, 1]
            tensorHeight = tensorShape[1];
            tensorWidth = tensorShape[2];
        }
        else
        {
            // NCHW format [batch, 1, height, width]
            tensorHeight = tensorShape[2];
            tensorWidth = tensorShape[3];
        }
        
        // Get tensor coordinates scaled to tensor dimensions
        int tx = Mathf.FloorToInt((float)x / textureWidth * tensorWidth);
        int ty = Mathf.FloorToInt((float)y / textureHeight * tensorHeight);
        
        // Clamp to valid range
        tx = Mathf.Clamp(tx, 0, tensorWidth - 1);
        ty = Mathf.Clamp(ty, 0, tensorHeight - 1);
        
        // Calculate index based on format
        if (isNHWC)
        {
            // Single-channel or multi-channel?
            int channels = tensorShape.rank >= 4 ? tensorShape[3] : 1;
            return (ty * tensorWidth * channels) + (tx * channels);
        }
        else
        {
            // Single-channel or multi-channel?
            return tensorShape.rank >= 4 
                ? (1 * tensorHeight * tensorWidth) + (ty * tensorWidth) + tx // Single output channel at index 1
                : (ty * tensorWidth) + tx; // No channel dimension
        }
    }
    
    /// <summary>
    /// Calculate tensor index for a specific pixel and channel (Legacy method kept for compatibility)
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
                if (input.shape.Length >= 4 && input.shape[3] == 3)
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