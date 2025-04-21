using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;
using System.Linq;

public class DeepLabPredictor : MonoBehaviour
{
    [Header("Model Settings")]
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
        ConvertOutputToMask(outputTensor, resultMask, selectedWallClassId);
        
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
            ConvertOutputToMask(outputTensor, resultMask, selectedWallClassId);
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
    
    private void ConvertOutputToMask(Tensor output, RenderTexture targetTexture, int? overrideClassId = null)
    {
        // Use provided class ID or default
        int classIdToUse = overrideClassId ?? wallClassId;
        
        // Log tensor shape for debugging
        Debug.Log($"Output tensor shape: {output.shape}");
        
        // Get raw data from tensor
        float[] rawData = output.data.Download(output.shape);
        
        // Create texture for mask
        Texture2D maskTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
        Color[] colors = new Color[inputWidth * inputHeight];
        
        // Counters for wall detection statistics
        int totalPixels = 0;
        int wallPixels = 0;
        
        // Handle different tensor formats
        // Special case for the (n:1, h:1, w:513, c:513) shape
        if (output.shape.height == 1 && output.shape.width == 513 && output.shape.channels == 513)
        {
            // Add debug log
            Debug.Log("Processing tensor with shape (1,1,513,513) - reshaping to 513x513");
            
            // If autoDetectWallClass is enabled, scan all possible classes
            Dictionary<int, int> classPixelCounts = new Dictionary<int, int>();
            
            // Reshape the tensor data to treat it as a flattened 513x513 image
            for (int y = 0; y < inputHeight; y++)
            {
                for (int x = 0; x < inputWidth; x++)
                {
                    int idx = y * inputWidth + x;
                    totalPixels++;
                    
                    if (idx < colors.Length && idx < rawData.Length)
                    {
                        float value = rawData[idx];
                        int classId = Mathf.RoundToInt(value);
                        
                        // Count pixels per class for auto-detection
                        if (autoDetectWallClass)
                        {
                            if (!classPixelCounts.ContainsKey(classId))
                                classPixelCounts[classId] = 0;
                            classPixelCounts[classId]++;
                        }
                        
                        bool isWall = (classId == classIdToUse);
                        if (isWall) wallPixels++;
                        
                        // Make walls visible with full white, other pixels transparent
                        colors[idx] = isWall ? Color.white : new Color(0, 0, 0, 0);
                        
                        // Log some wall pixels for debugging
                        if (isWall && enableDebugLogging && idx % 10000 == 0)
                        {
                            Debug.Log($"Wall pixel detected at ({x},{y}), value={value}, classId={classId}");
                        }
                    }
                }
            }
            
            // If auto-detection is enabled, log statistics about classes
            if (autoDetectWallClass && classPixelCounts.Count > 0)
            {
                Debug.Log("Class distribution in segmentation output:");
                foreach (var kvp in classPixelCounts.OrderByDescending(k => k.Value))
                {
                    float percentage = (float)kvp.Value / totalPixels * 100f;
                    Debug.Log($"  Class {kvp.Key}: {kvp.Value} pixels ({percentage:F2}%)");
                    
                    // If this class has significant coverage (>5%), suggest it might be walls
                    if (percentage > 3f && kvp.Key != 0)
                    {
                        Debug.Log($"  Class {kvp.Key} has significant coverage and might represent walls. Consider setting wallClassId to {kvp.Key}.");
                        
                        // Auto-update wall class if we found a good candidate and haven't detected walls yet
                        if (!hasDetectedWalls && percentage > 5f)
                        {
                            Debug.Log($"Auto-selecting class {kvp.Key} as wall class");
                            selectedWallClassId = kvp.Key;
                            
                            // Reprocess with this class ID
                            for (int i = 0; i < colors.Length; i++)
                            {
                                if (i < rawData.Length)
                                {
                                    bool isWall = Mathf.RoundToInt(rawData[i]) == selectedWallClassId;
                                    colors[i] = isWall ? Color.white : new Color(0, 0, 0, 0);
                                }
                            }
                            wallPixels = classPixelCounts[selectedWallClassId]; // Update wall pixel count
                        }
                    }
                }
            }
            
            // Count how many wall pixels we found for debugging
            float wallPercentage = (float)wallPixels / totalPixels * 100f;
            Debug.Log($"Wall pixels found: {wallPixels} out of {totalPixels} ({wallPercentage:F2}%)");
            
            // Update our detection status for future frames
            hasDetectedWalls = wallPixels > 100;
            
            // If very few wall pixels are found, suggest trying different class ID
            if (wallPixels < 100 && !autoDetectWallClass)
            {
                Debug.LogWarning($"Very few wall pixels detected with current wallClassId={classIdToUse}. Try enabling autoDetectWallClass or manually trying different values for wallClassId.");
            }
        }
        // Original single-channel format
        else if (output.shape.channels == 1)
        {
            Debug.Log("Processing single-channel tensor");
            // Single-channel tensor (already argmax'd)
            for (int i = 0; i < Mathf.Min(rawData.Length, colors.Length); i++)
            {
                totalPixels++;
                int classId = Mathf.RoundToInt(rawData[i]);
                bool isWall = (classId == classIdToUse);
                if (isWall) wallPixels++;
                colors[i] = isWall ? Color.white : new Color(0, 0, 0, 0);
            }
            
            // Count how many wall pixels for debugging
            float wallPercentage = (float)wallPixels / totalPixels * 100f;
            Debug.Log($"Wall pixels found: {wallPixels} out of {totalPixels} ({wallPercentage:F2}%)");
            
            // Update our detection status
            hasDetectedWalls = wallPixels > 100;
        }
        // Original multi-channel format
        else if (output.shape.channels > 1)
        {
            Debug.Log($"Processing multi-channel tensor with {output.shape.channels} channels");
            // Multi-channel tensor (class probabilities)
            int numClasses = output.shape.channels;
            Dictionary<int, int> classPixelCounts = new Dictionary<int, int>();
            
            for (int y = 0; y < inputHeight; y++)
            {
                for (int x = 0; x < inputWidth; x++)
                {
                    totalPixels++;
                    int pixelIdx = y * inputWidth + x;
                    int maxClassId = 0;
                    float maxProb = float.MinValue;
                    
                    // Find the class with highest probability
                    for (int c = 0; c < numClasses; c++)
                    {
                        int idx = (pixelIdx * numClasses) + c;
                        if (idx < rawData.Length)
                        {
                            float prob = rawData[idx];
                            if (prob > maxProb)
                            {
                                maxProb = prob;
                                maxClassId = c;
                            }
                        }
                    }
                    
                    // If auto-detection is enabled, track class counts
                    if (autoDetectWallClass)
                    {
                        if (!classPixelCounts.ContainsKey(maxClassId))
                            classPixelCounts[maxClassId] = 0;
                        classPixelCounts[maxClassId]++;
                    }
                    
                    // Set color based on whether it's a wall or not
                    bool isWall = (maxClassId == classIdToUse && maxProb > classificationThreshold);
                    if (isWall) wallPixels++;
                    colors[pixelIdx] = isWall ? Color.white : new Color(0, 0, 0, 0);
                }
            }
            
            // If auto-detection is enabled, log statistics about classes
            if (autoDetectWallClass && classPixelCounts.Count > 0)
            {
                Debug.Log("Class distribution in segmentation output:");
                foreach (var kvp in classPixelCounts.OrderByDescending(k => k.Value))
                {
                    float percentage = (float)kvp.Value / totalPixels * 100f;
                    Debug.Log($"  Class {kvp.Key}: {kvp.Value} pixels ({percentage:F2}%)");
                    
                    // Auto-update wall class if we found a good candidate and haven't detected walls yet
                    if (!hasDetectedWalls && percentage > 5f && kvp.Key != 0)
                    {
                        Debug.Log($"Auto-selecting class {kvp.Key} as wall class");
                        selectedWallClassId = kvp.Key;
                        
                        // Reprocess with this class ID for this frame
                        for (int y = 0; y < inputHeight; y++)
                        {
                            for (int x = 0; x < inputWidth; x++)
                            {
                                int pixelIdx = y * inputWidth + x;
                                int cls = 0;
                                float maxProb = float.MinValue;
                                
                                // Determine class ID for this pixel
                                for (int c = 0; c < numClasses; c++)
                                {
                                    int idx = (pixelIdx * numClasses) + c;
                                    if (idx < rawData.Length && rawData[idx] > maxProb)
                                    {
                                        maxProb = rawData[idx];
                                        cls = c;
                                    }
                                }
                                
                                // Update color based on new wall class
                                colors[pixelIdx] = (cls == selectedWallClassId && maxProb > classificationThreshold) 
                                                ? Color.white 
                                                : new Color(0, 0, 0, 0);
                            }
                        }
                    }
                }
            }
            
            // Update wall detection status
            hasDetectedWalls = wallPixels > 100;
            
            // Count walls for debugging
            float wallPercentage = (float)wallPixels / totalPixels * 100f;
            Debug.Log($"Wall pixels found: {wallPixels} out of {totalPixels} ({wallPercentage:F2}%)");
        }
        
        // Set the colors to the texture with fully opaque alpha to ensure visibility
        for (int i = 0; i < colors.Length; i++)
        {
            if (colors[i].r > 0.5f || colors[i].g > 0.5f || colors[i].b > 0.5f)
            {
                colors[i].a = 1.0f;  // Make sure wall pixels are fully opaque
            }
        }
        
        maskTexture.SetPixels(colors);
        maskTexture.Apply();
        
        // Copy to RenderTexture
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = targetTexture;
        
        GL.Clear(true, true, Color.clear);
        Graphics.Blit(maskTexture, targetTexture);
        
        RenderTexture.active = prevRT;
        
        // Cleanup
        Destroy(maskTexture);
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
} 