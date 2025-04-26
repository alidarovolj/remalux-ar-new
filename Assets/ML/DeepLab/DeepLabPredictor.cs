using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;
using System.Linq;

public class DeepLabPredictor : MonoBehaviour
{
    [Header("Model Settings")]
    [Tooltip("Assign the model.onnx asset here. Do not use any other model file.")]
    public NNModel modelAsset;
    private Model runtimeModel;
    private IWorker engine;

    [Header("Input/Output Settings")]
    public int inputWidth = 512;
    public int inputHeight = 512;
    
    [Tooltip("Name of input tensor in the ONNX model")]
    public string inputName = "ImageTensor";
    
    [Tooltip("Name of the output tensor in the ONNX model")]
    public string outputName = "SemanticPredictions";
    
    [Header("Segmentation Settings")]
    [SerializeField] protected int wallClassId = 9;
    [SerializeField] protected int[] alternateWallClassIds = { 5, 3, 1, 9, 8, 12, 2, 4 };
    [SerializeField] protected float classificationThreshold = 0.03f;
    [SerializeField] protected bool useArgMax = true;
    [SerializeField] protected bool temporalSmoothing = true;
    [SerializeField] [Range(0, 1)] protected float smoothingFactor = 0.7f;
    [SerializeField] protected bool noiseReduction = true;
    [SerializeField] [Range(0, 10)] protected int noiseReductionKernelSize = 5;
    
    // Public properties to access protected fields
    public virtual int WallClassId { 
        get { return wallClassId; }
        set { 
            if (wallClassId != value) {
                wallClassId = value;
                OnWallClassIdChanged?.Invoke((byte)wallClassId);
            }
        }
    }
    
    public virtual float ClassificationThreshold {
        get { return classificationThreshold; }
        set { classificationThreshold = value; }
    }
    
    public virtual bool UseArgMaxMode {
        get { return useArgMax; }
        set { useArgMax = value; }
    }
    
    public virtual bool UseTemporalSmoothing {
        get { return temporalSmoothing; }
        set { temporalSmoothing = value; }
    }
    
    public virtual float TemporalSmoothingFactor {
        get { return smoothingFactor; }
        set { smoothingFactor = value; }
    }
    
    public virtual bool UseNoiseReduction {
        get { return noiseReduction; }
        set { noiseReduction = value; }
    }
    
    public virtual int NoiseReductionKernel {
        get { return noiseReductionKernelSize; }
        set { noiseReductionKernelSize = value; }
    }
    
    // Event for wall class ID changes
    public virtual event System.Action<byte> OnWallClassIdChanged;
    
    [Header("Debug Settings")]
    [Tooltip("Enable detailed logging")]
    public bool enableDebugLogging = true;
    [Tooltip("Try all possible class IDs to find walls")]
    public bool autoDetectWallClass = true; // Enable auto-detection by default
    [Tooltip("Save debug images to disk")]
    public bool saveDebugImages = false;

    private RenderTexture resultMask;
    private Texture2D outputTexture;
    private Dictionary<string, string> outputNodeNames = new Dictionary<string, string>
    {
        { "SemanticPredictions", "SemanticPredictions" },   // Default name in DeepLabV3
        { "ArgMax", "ArgMax" },                           // Alternative name
        { "final_output", "final_output" },               // Another possible name
        { "softmax", "softmax" },                         // Layer before ArgMax
        { "logits", "logits" }                            // Output layer before softmax
    };
    
    // Name of output node that works with current model
    private string workingOutputName = "SemanticPredictions";
    private int frameCount = 0;
    private int selectedWallClassId;
    private bool hasDetectedWalls = false;

    protected virtual void Awake()
    {
        // Validate model asset
        ValidateModelAsset();
    }

    protected virtual void ValidateModelAsset()
    {
        if (modelAsset == null)
        {
            Debug.LogError("DeepLabPredictor: No model asset assigned! Please assign model.onnx in the inspector.");
            enabled = false;
            return;
        }

        // Verify this is the correct model (model.onnx)
        if (!modelAsset.name.Contains("model"))
        {
            Debug.LogWarning($"DeepLabPredictor: The model '{modelAsset.name}' is being used, but 'model.onnx' is the only fully supported model for this project. This may cause issues with wall detection and segmentation. Please update references to use model.onnx.");
        }
    }

    protected virtual void Start()
    {
        if (modelAsset == null)
        {
            Debug.LogError("DeepLabV3 model asset is not assigned!");
            return;
        }
        
        Initialize();
        selectedWallClassId = wallClassId; // Start with the specified class ID
    }

    protected virtual void OnEnable()
    {
        // Base implementation
    }

    protected virtual void OnDisable()
    {
        // Base implementation
    }

    public void Initialize()
    {
        // Load the model from ONNX
        runtimeModel = ModelLoader.Load(modelAsset);
        
        // Create worker with appropriate backend
        engine = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
        
        // Initialize result texture with depth buffer
        resultMask = new RenderTexture(inputWidth, inputHeight, 24, RenderTextureFormat.ARGB32);
        resultMask.enableRandomWrite = true;
        resultMask.Create();
        
        // Determine available output nodes
        workingOutputName = FindWorkingOutputLayer();
        
        Debug.Log("DeepLabV3 model initialized successfully");
        Debug.Log($"Model outputs: {string.Join(", ", runtimeModel.outputs)}");
    }

    private string FindWorkingOutputLayer()
    {
        // Log all available output layers in the model
        Debug.Log($"Available model outputs ({runtimeModel.outputs.Count}):");
        foreach (var output in runtimeModel.outputs)
        {
            Debug.Log($"  - {output}");
        }
        
        // Try to find matching output layer names
        foreach (var nodeName in outputNodeNames.Keys)
        {
            try
            {
                foreach (var output in runtimeModel.outputs)
                {
                    if (output.Contains(nodeName))
                    {
                        Debug.Log($"Using output layer: {output}");
                        return output;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error checking output layer {nodeName}: {e.Message}");
            }
        }
        
        // Use first output layer if none of the known names match
        if (runtimeModel.outputs.Count > 0)
        {
            string firstOutput = runtimeModel.outputs[0];
            Debug.Log($"No known output layers found. Using first available: {firstOutput}");
            return firstOutput;
        }
        
        // Fallback to standard name
        Debug.LogWarning("Cannot determine model output layer. Using default name.");
        return "SemanticPredictions";
    }

    public RenderTexture PredictSegmentation(Texture2D inputTexture)
    {
        if (engine == null || inputTexture == null)
        {
            Debug.LogError("DeepLabV3 engine or input texture is null");
            return null;
        }
        
        frameCount++;
        if (enableDebugLogging && frameCount % 30 == 0)
        {
            Debug.Log($"Processing frame {frameCount} with input texture size {inputTexture.width}x{inputTexture.height}");
        }
        
        // Clear the result mask before processing
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = resultMask;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prevRT;
        
        // Resize input to model dimensions
        Texture2D resized = ResizeTexture(inputTexture, inputWidth, inputHeight);
        
        // Save input texture for debugging
        if (saveDebugImages && frameCount % 30 == 0)
        {
            SaveTextureToFile(resized, $"deeplab_input_{frameCount}.png");
        }
        
        // Convert to Tensor
        Tensor inputTensor = PreprocessTexture(resized);
        
        // Run inference
        engine.Execute(inputTensor);
        
        // Get output tensor
        Tensor outputTensor = null;
        
        try
        {
            // Try to get tensor by working name
            outputTensor = engine.PeekOutput(workingOutputName);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error getting output tensor '{workingOutputName}': {e.Message}");
            
            // Try to get first output tensor
            try
            {
                // Log available model outputs
                Debug.Log("Available model outputs:");
                foreach (var output in runtimeModel.outputs)
                {
                    Debug.Log($"  - {output}");
                }
                
                // Just get first output without specifying name
                outputTensor = engine.PeekOutput();
                Debug.Log("Using PeekOutput without parameters");
            }
            catch (System.Exception e2)
            {
                Debug.LogError($"Failed to get model outputs: {e2.Message}");
                inputTensor.Dispose();
                return null;
            }
        }
        
        if (outputTensor == null)
        {
            Debug.LogError("Failed to get output tensor!");
            inputTensor.Dispose();
            return null;
        }
        
        // Process output tensor to mask
        ProcessOutputToMask(outputTensor, resultMask);
        
        // If we're struggling to detect walls, try rotating through different class IDs
        if (!hasDetectedWalls && alternateWallClassIds.Length > 0 && frameCount % 60 == 0)
        {
            // Try next class ID in the list
            int currentIndex = System.Array.IndexOf(alternateWallClassIds, selectedWallClassId);
            if (currentIndex == -1 || currentIndex >= alternateWallClassIds.Length - 1)
                selectedWallClassId = alternateWallClassIds[0];
            else
                selectedWallClassId = alternateWallClassIds[currentIndex + 1];
                
            Debug.Log($"Trying alternate wall class ID: {selectedWallClassId}");
            
            // Reprocess the tensor with new class ID
            ProcessOutputToMask(outputTensor, resultMask);
        }
        
        // Save output for debugging
        if (saveDebugImages && frameCount % 30 == 0)
        {
            SaveRenderTextureToFile(resultMask, $"wall_mask_{frameCount}.png");
        }
        
        // Cleanup
        inputTensor.Dispose();
        outputTensor.Dispose();
        
        if (resized != inputTexture)
        {
            Destroy(resized);
        }
        
        return resultMask;
    }
    
    private Tensor PreprocessTexture(Texture2D texture)
    {
        // Create tensor with appropriate dimensions [1, height, width, 3]
        Tensor tensor = new Tensor(1, inputHeight, inputWidth, 3);
        
        // RGB values for the network (normalized to [-1, 1] or [0, 1] depending on model)
        float[] pixels = texture.GetPixels().SelectMany(color => new[] { color.r, color.g, color.b }).ToArray();
        
        // Upload to tensor
        tensor.data.Upload(pixels, new TensorShape(1, inputHeight, inputWidth, 3));
        
        return tensor;
    }
    
    /// <summary>
    /// Process the output tensor to create a mask
    /// </summary>
    private void ProcessOutputToMask(Tensor output, RenderTexture targetTexture)
    {
        // Process the output tensor to create a segmentation mask
        // This implementation will vary based on the output format of your model
        
        try
        {
            // Create temporary texture for processing
            Texture2D outputTex = new Texture2D(output.width, output.height, TextureFormat.R8, false);
            
            // Get data from tensor
            float[] outputData = output.AsFloats();
            byte[] pixels = new byte[output.width * output.height];
            
            // Process output based on format
            if (useArgMax)
            {
                // Model output is already class IDs (one per pixel)
                for (int i = 0; i < pixels.Length; i++)
                {
                    int classId = Mathf.RoundToInt(outputData[i]);
                    
                    // Check if this pixel is a wall (using current wallClassId or auto-detect)
                    bool isWall = false;
                    
                    if (autoDetectWallClass && !hasDetectedWalls)
                    {
                        // Try all alternative wall class IDs if auto-detect is enabled
                        isWall = classId == selectedWallClassId;
                        
                        // If not matching current selection, check alternates
                        if (!isWall)
                        {
                            foreach (int altClass in alternateWallClassIds)
                            {
                                if (classId == altClass)
                                {
                                    if (enableDebugLogging && selectedWallClassId != altClass)
                                    {
                                        selectedWallClassId = altClass;
                                        Debug.Log($"Detected potential wall with class ID: {altClass}");
                                    }
                                    isWall = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Just check against the specified wall class ID
                        isWall = classId == wallClassId;
                    }
                    
                    // Set pixel value (255 for wall, 0 for other)
                    pixels[i] = isWall ? (byte)255 : (byte)0;
                }
            }
            else
            {
                // Model output is probabilities/logits
                // Determine output format based on channel count
                int numClasses = output.channels;
                
                if (numClasses > 1)
                {
                    // Multi-class output (softmax/logits)
                    for (int y = 0; y < output.height; y++)
                    {
                        for (int x = 0; x < output.width; x++)
                        {
                            int pixelIdx = y * output.width + x;
                            
                            // Get probability for each class at this pixel
                            float maxProb = 0f;
                            int maxClass = 0;
                            
                            // Find class with highest probability
                            for (int c = 0; c < numClasses; c++)
                            {
                                int idx = (c * output.height * output.width) + pixelIdx;
                                float prob = outputData[idx];
                                
                                if (prob > maxProb)
                                {
                                    maxProb = prob;
                                    maxClass = c;
                                }
                            }
                            
                            // Check if max class is wall and above threshold
                            bool isWall = false;
                            
                            if (autoDetectWallClass && !hasDetectedWalls)
                            {
                                // Auto-detect mode
                                if (maxClass == selectedWallClassId && maxProb >= classificationThreshold)
                                {
                                    isWall = true;
                                }
                                else
                                {
                                    // Check alternates
                                    foreach (int altClass in alternateWallClassIds)
                                    {
                                        float altProb = 0f;
                                        int idx = (altClass * output.height * output.width) + pixelIdx;
                                        
                                        if (altClass < numClasses)
                                        {
                                            altProb = outputData[idx];
                                        }
                                        
                                        if (altProb >= classificationThreshold && altProb > maxProb)
                                        {
                                            if (enableDebugLogging && selectedWallClassId != altClass)
                                            {
                                                selectedWallClassId = altClass;
                                                Debug.Log($"Detected potential wall with class ID: {altClass}");
                                            }
                                            isWall = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Just check specified wall class
                                int idx = (wallClassId * output.height * output.width) + pixelIdx;
                                if (wallClassId < numClasses)
                                {
                                    float wallProb = outputData[idx];
                                    isWall = wallProb >= classificationThreshold;
                                }
                            }
                            
                            // Set pixel value
                            pixels[pixelIdx] = isWall ? (byte)255 : (byte)0;
                        }
                    }
                }
                else
                {
                    // Single-channel output (binary)
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = outputData[i] >= classificationThreshold ? (byte)255 : (byte)0;
                    }
                }
            }
            
            // Apply noise reduction if enabled
            if (noiseReduction && noiseReductionKernelSize > 0)
            {
                pixels = ApplyNoiseReduction(pixels, output.width, output.height);
            }
            
            // Update texture with processed data
            outputTex.LoadRawTextureData(pixels);
            outputTex.Apply();
            
            // Copy to render texture
            Graphics.Blit(outputTex, targetTexture);
            
            // Cleanup
            Destroy(outputTex);
            
            // Mark that we've detected walls if auto-detect is enabled
            if (autoDetectWallClass && !hasDetectedWalls)
            {
                // Check if we've found walls
                int wallPixels = 0;
                foreach (byte p in pixels)
                {
                    if (p > 0) wallPixels++;
                }
                
                // If we found a significant number of wall pixels, consider detection success
                if (wallPixels > pixels.Length * 0.05f)
                {
                    hasDetectedWalls = true;
                    wallClassId = selectedWallClassId;
                    Debug.Log($"Wall class auto-detection successful. Using class ID: {wallClassId}");
                }
            }
            
            // Debug
            if (enableDebugLogging && frameCount % 30 == 0)
            {
                int wallCount = pixels.Count(p => p > 0);
                float wallPercentage = (float)wallCount / pixels.Length * 100f;
                Debug.Log($"Wall detection: {wallCount} pixels ({wallPercentage:F1}%) using class ID {wallClassId}");
            }
            
            // Save debug output if enabled
            if (saveDebugImages)
            {
                SaveRenderTextureToFile(targetTexture, $"segmentation_{System.DateTime.Now.Ticks}.png");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error processing segmentation output: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        if (source.width == width && source.height == height)
            return source;
            
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
        Graphics.Blit(source, rt);
        
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        resized.Apply();
        
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);
        
        return resized;
    }
    
    private void SaveTextureToFile(Texture2D texture, string filename)
    {
        if (!saveDebugImages) return;
        
        #if UNITY_EDITOR
        byte[] bytes = texture.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log($"Saved debug texture to {path}");
        #endif
    }
    
    private void SaveRenderTextureToFile(RenderTexture rt, string filename)
    {
        if (!saveDebugImages) return;
        
        #if UNITY_EDITOR
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        
        RenderTexture.active = prevRT;
        
        byte[] bytes = tex.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllBytes(path, bytes);
        
        Debug.Log($"Saved debug render texture to {path}");
        Destroy(tex);
        #endif
    }

    private void OnDestroy()
    {
        if (engine != null)
        {
            engine.Dispose();
            engine = null;
        }
        
        if (resultMask != null)
        {
            resultMask.Release();
        }
    }

    // Add noise reduction method
    private byte[] ApplyNoiseReduction(byte[] pixels, int width, int height)
    {
        // Simple median filter implementation for noise reduction
        byte[] result = new byte[pixels.Length];
        System.Array.Copy(pixels, result, pixels.Length);
        
        int kernelSize = Mathf.Min(noiseReductionKernelSize, 5); // Cap at 5x5 for performance
        int kernelRadius = kernelSize / 2;
        
        // Apply median filter to reduce noise
        for (int y = kernelRadius; y < height - kernelRadius; y++)
        {
            for (int x = kernelRadius; x < width - kernelRadius; x++)
            {
                int centerIdx = y * width + x;
                
                // For binary mask, we can just count neighbors instead of full median
                int wallCount = 0;
                int totalCount = 0;
                
                // Check neighborhood
                for (int ky = -kernelRadius; ky <= kernelRadius; ky++)
                {
                    for (int kx = -kernelRadius; kx <= kernelRadius; kx++)
                    {
                        int idx = (y + ky) * width + (x + kx);
                        if (idx >= 0 && idx < pixels.Length)
                        {
                            totalCount++;
                            if (pixels[idx] > 0)
                                wallCount++;
                        }
                    }
                }
                
                // Set pixel based on majority vote
                float wallRatio = (float)wallCount / totalCount;
                result[centerIdx] = wallRatio > 0.5f ? (byte)255 : (byte)0;
            }
        }
        
        return result;
    }
} 