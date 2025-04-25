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
    [SerializeField] private NNModel modelAsset;
    [SerializeField] private string inputName = "ImageTensor";
    [SerializeField] private string outputName = "SemanticPredictions";
    [SerializeField] private int inputWidth = 224;
    [SerializeField] private int inputHeight = 224;
    
    [Header("Segmentation Settings")]
    [SerializeField] private byte wallClassId = 9; // ADE20K wall class ID
    [SerializeField] private bool useComputeOptimization = true;
    [SerializeField] private int processingInterval = 5; // Process every N frames
    
    [Header("Performance")]
    [SerializeField] private WorkerFactory.Type workerType = WorkerFactory.Type.ComputePrecompiled;
    [Tooltip("Set 0 for automatic device selection")]
    [SerializeField] private int computeDeviceIndex = 0;
    
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
        
        // Load model
        _runtimeModel = ModelLoader.Load(modelAsset);
        
        // Check for valid input/output names
        bool validInputs = false;
        bool validOutputs = false;
        
        foreach (var input in _runtimeModel.inputs)
        {
            if (input.name.Contains(inputName))
            {
                inputName = input.name;
                validInputs = true;
                break;
            }
        }
        
        foreach (var output in _runtimeModel.outputs)
        {
            if (output.Contains(outputName))
            {
                outputName = output;
                validOutputs = true;
                break;
            }
        }
        
        if (!validInputs || !validOutputs)
        {
            Debug.LogError($"Model doesn't contain expected input/output layers. Inputs: {string.Join(", ", _runtimeModel.inputs)} Outputs: {string.Join(", ", _runtimeModel.outputs)}");
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
            // Create a specific compute configuration if needed
            var workerOptions = computeDeviceIndex > 0 
                ? WorkerFactory.ValidationDeviceType.ValidateGpuDevice(backend, computeDeviceIndex) 
                : WorkerFactory.ValidateType(backend);
                
            _engine = WorkerFactory.CreateWorker(workerOptions, _runtimeModel);
            Debug.Log($"SegmentationManager: Created ML engine using {backend} backend");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create ML worker: {e.Message}. Falling back to CPU.");
            _engine = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, _runtimeModel);
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
            Debug.LogError($"Error during segmentation: {e.Message}");
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
        
        // Process each pixel to find wall class
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++) 
            {
                int pixelIndex = y * width + x;
                
                // Expected format: [batch, width, height, channels]
                // For segmentation, each pixel has a class ID value
                float classId = data[pixelIndex];
                
                // Check if this pixel is a wall
                byte value = classId == wallClassId ? (byte)255 : (byte)0;
                
                // Set all channels for compatibility
                pixels[pixelIndex] = new Color32(value, value, value, 255);
            }
        }
        
        // Apply to texture
        texture.SetPixels32(pixels);
        texture.Apply();
    }
    
    private void OnDestroy()
    {
        // Clean up Barracuda resources
        _engine?.Dispose();
        
        // Clean up textures
        if (_resizedInput != null)
            Destroy(_resizedInput);
            
        if (_outputTexture != null)
            Destroy(_outputTexture);
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
#endif
} 