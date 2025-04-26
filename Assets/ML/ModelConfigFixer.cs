using UnityEngine;
using ML;
using Unity.Barracuda;
using ML.DeepLab;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Component to automatically fix SegmentationManager configuration based on model inspection
/// </summary>
public class ModelConfigFixer : MonoBehaviour
{
    [SerializeField] public SegmentationManager segmentationManager;
    [SerializeField] private EnhancedDeepLabPredictor enhancedPredictor;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private NNModel modelAsset;
    [SerializeField] private bool fixOnAwake = false;
    [SerializeField] private bool debugLogging = true;

    private void Awake()
    {
        if (fixOnAwake)
        {
            FixModelConfiguration();
        }
    }

    public void FixModelConfiguration()
    {
        if (segmentationManager == null)
        {
            Debug.LogError("No SegmentationManager assigned to ModelConfigFixer");
            return;
        }

        if (modelAsset == null)
        {
            modelAsset = segmentationManager.ModelAsset;
            if (modelAsset == null)
            {
                Debug.LogError("No model asset found in SegmentationManager or ModelConfigFixer");
                return;
            }
        }

        // Load model to inspect its properties
        Model runtimeModel = null;
        try
        {
            runtimeModel = ModelLoader.Load(modelAsset);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load model for inspection: {e.Message}");
            return;
        }

        if (runtimeModel == null)
        {
            Debug.LogError("Failed to load runtime model");
            return;
        }

        // Check if model has inputs
        if (runtimeModel.inputs.Count == 0)
        {
            Debug.LogError("Model has no inputs");
            return;
        }

        // Create a temporary worker to examine tensors
        int inputWidth = 256;
        int inputHeight = 256;
        int inputChannels = 3;
        bool formatIsNHWC = true; // Default format, renamed from isNHWC to avoid redeclaration
        
        // Create a temporary worker to inspect the model
        IWorker tempWorker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);

        // Create a dummy input to force tensor allocation
        var dummyInput = new Tensor(1, inputHeight, inputWidth, inputChannels);
        tempWorker.Execute(dummyInput);
        
        // Now try to peek at the input to get actual shape info
        try
        {
            string firstInputName = runtimeModel.inputs[0].name;
            Tensor inputTensor = tempWorker.PeekOutput(firstInputName);
            
            if (inputTensor != null)
            {
                // The dimensions are accessible through the shape property 
                TensorShape shape = inputTensor.shape;
                int[] dimensions = shape.dimensions;
                
                // Now we can safely check the length
                if (dimensions != null && dimensions.Length >= 4)
                {
                    if (debugLogging)
                    {
                        Debug.Log($"Input tensor dimensions: {string.Join(" x ", dimensions)}");
                    }
                    
                    // Format detection: NHWC vs NCHW
                    // Try to determine format by looking at channel position (smallest dimension is usually channels)
                    int n = dimensions[0];
                    int dim1 = dimensions[1];
                    int dim2 = dimensions[2];
                    int dim3 = dimensions[3];
                    
                    // Log all dimensions for debugging
                    if (debugLogging)
                    {
                        Debug.Log($"Dimensions: n={n}, dim1={dim1}, dim2={dim2}, dim3={dim3}");
                    }
                    
                    bool isNHWC = false;
                    
                    // If dim3 is significantly smaller than the others, it's likely NHWC
                    // If dim1 is significantly smaller, it's likely NCHW
                    if (dim3 == 3 || (dim3 <= 4 && dim1 > 16 && dim2 > 16))
                    {
                        isNHWC = true;
                        inputHeight = dim1;
                        inputWidth = dim2;
                        if (debugLogging)
                        {
                            Debug.Log("Detected NHWC format");
                        }
                    }
                    else if (dim1 == 3 || (dim1 <= 4 && dim2 > 16 && dim3 > 16))
                    {
                        isNHWC = false;
                        inputHeight = dim2;
                        inputWidth = dim3;
                        if (debugLogging)
                        {
                            Debug.Log("Detected NCHW format");
                        }
                    }
                    else
                    {
                        // If format can't be clearly determined, use sizes to guess
                        // Usually the height and width are similar in size
                        if (System.Math.Abs(dim2 - dim3) < System.Math.Abs(dim1 - dim2))
                        {
                            // dim2 and dim3 are more similar, so likely H and W
                            isNHWC = true;
                            inputHeight = dim1;
                            inputWidth = dim2;
                            if (debugLogging)
                            {
                                Debug.Log("Format unclear, guessing NHWC based on dimension similarity");
                            }
                        }
                        else
                        {
                            isNHWC = false;
                            inputHeight = dim2;
                            inputWidth = dim3;
                            if (debugLogging)
                            {
                                Debug.Log("Format unclear, guessing NCHW based on dimension similarity");
                            }
                        }
                    }
                    
                    // Update the formatIsNHWC from the outer scope
                    formatIsNHWC = isNHWC;
                }
            }
        }
        catch (System.Exception e)
        {
            if (debugLogging)
            {
                Debug.LogWarning($"Could not peek at model tensor shapes: {e.Message}. Using default dimensions.");
            }
        }
        
        // Clean up temporary objects
        dummyInput.Dispose();
        tempWorker.Dispose();

        // Check outputs
        if (runtimeModel.outputs.Count == 0)
        {
            Debug.LogError("Model has no outputs");
            return;
        }

        // Get first output name as default
        string outputName = runtimeModel.outputs[0];

        // Log available outputs
        if (debugLogging)
        {
            Debug.Log("Available output tensors:");
            for (int i = 0; i < runtimeModel.outputs.Count; i++)
            {
                Debug.Log($"  {i}: {runtimeModel.outputs[i]}");
            }
        }

        // Common output names for segmentation models
        string[] commonOutputNames = new string[] { "logits", "output", "segmentation", "output_segmentations", "predictions", "final" };
        foreach (var output in runtimeModel.outputs)
        {
            foreach (var name in commonOutputNames)
            {
                if (output.ToLower().Contains(name))
                {
                    outputName = output;
                    if (debugLogging)
                    {
                        Debug.Log($"Selected output '{outputName}' based on common name match");
                    }
                    break;
                }
            }
        }

        // Apply configuration to SegmentationManager
        segmentationManager.inputWidth = inputWidth;
        segmentationManager.inputHeight = inputHeight;
        segmentationManager.inputChannels = inputChannels;
        segmentationManager.outputName = outputName;
        segmentationManager.isModelNHWCFormat = formatIsNHWC;

        if (debugLogging)
        {
            Debug.Log($"Applied configuration to SegmentationManager: InputWidth={inputWidth}, InputHeight={inputHeight}, InputChannels={inputChannels}, OutputName={outputName}, IsNHWC={formatIsNHWC}");
        }
    }
    
    // [ContextMenu("Log Model Shapes")]
    public void LogModelShapes()
    {
        if (modelAsset == null)
        {
            Debug.LogError("Model asset is not assigned!");
            return;
        }
        
        var model = ModelLoader.Load(modelAsset);
        Debug.Log("=== MODEL SHAPE INFORMATION ===");
        
        // Log inputs
        Debug.Log($"Model has {model.inputs.Count} input(s):");
        foreach (var input in model.inputs)
        {
            Debug.Log($"Input name: {input.name}");
            
            // Note: We can't get shape information without creating a tensor
        }
        
        // Log outputs
        Debug.Log($"Model has {model.outputs.Count} output(s):");
        foreach (var output in model.outputs)
        {
            Debug.Log($"Output name: {output}");
            
            // Note: We can't get shape information without creating a tensor
        }
        
        // Log current SegmentationManager settings
        if (segmentationManager != null)
        {
            Debug.Log("Current SegmentationManager settings:");
            Debug.Log($"Input dimensions: {segmentationManager.inputWidth}x{segmentationManager.inputHeight}x{segmentationManager.inputChannels}");
            Debug.Log($"Output name: {segmentationManager.outputName}");
            Debug.Log($"Wall class ID: {segmentationManager.wallClassId}");
        }
        
        Debug.Log("=== END OF MODEL SHAPE INFORMATION ===");
    }

    public void LogModelStructure()
    {
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager is not assigned!");
            return;
        }

        var model = segmentationManager.ModelAsset;
        if (model == null)
        {
            Debug.LogError("Model is not assigned in SegmentationManager!");
            return;
        }

        try
        {
            var runtimeModel = ModelLoader.Load(model);
            
            Debug.Log("========== MODEL STRUCTURE ==========");
            Debug.Log($"Model: {model.name}");
            
            // Log inputs
            Debug.Log("--- Inputs ---");
            foreach (var input in runtimeModel.inputs)
            {
                Debug.Log($"Input: {input}");
            }
            
            // Log outputs
            Debug.Log("--- Outputs ---");
            foreach (var output in runtimeModel.outputs)
            {
                Debug.Log($"Output: {output}");
            }
            
            // Log layers - we can't access detailed layer info without ModelLayer type
            Debug.Log("--- Layers ---");
            Debug.Log("Cannot access detailed layer information. Check model documentation for layer structure.");
            
            Debug.Log("===============================");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load model for inspection: {e.Message}");
        }
    }
    
    public void AnalyzeReshapeError()
    {
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager is not assigned!");
            return;
        }

        var model = segmentationManager.ModelAsset;
        if (model == null)
        {
            Debug.LogError("Model is not assigned in SegmentationManager!");
            return;
        }

        try
        {
            var runtimeModel = ModelLoader.Load(model);
            
            Debug.Log("========== RESHAPE ERROR ANALYSIS ==========");
            
            // Create a dummy worker to try to get tensor info
            IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
            
            // Make a placeholder tensor for execution 
            int inputWidth = segmentationManager.inputWidth;
            int inputHeight = segmentationManager.inputHeight;
            int inputChannels = segmentationManager.inputChannels;
            bool isNHWC = segmentationManager.isModelNHWCFormat;
            
            Debug.Log($"Current SegmentationManager settings:");
            Debug.Log($"- Input Width: {inputWidth}");
            Debug.Log($"- Input Height: {inputHeight}");
            Debug.Log($"- Input Channels: {inputChannels}");
            Debug.Log($"- Output Name: {segmentationManager.outputName}");
            Debug.Log($"- Is NHWC format: {isNHWC}");
            
            // Try with a dummy tensor
            try
            {
                Tensor dummyInput;
                if (isNHWC)
                {
                    dummyInput = new Tensor(1, inputHeight, inputWidth, inputChannels);
                }
                else
                {
                    dummyInput = new Tensor(1, inputChannels, inputHeight, inputWidth);
                }
                
                worker.Execute(dummyInput);
                
                // Check if the output tensor exists
                string outputName = segmentationManager.outputName;
                bool outputExists = false;
                
                List<string> outputNames = new List<string>();
                foreach (var output in runtimeModel.outputs)
                {
                    outputNames.Add(output);
                    if (output == outputName)
                    {
                        outputExists = true;
                    }
                }

                if (!outputExists)
                {
                    Debug.LogWarning($"Output tensor with name '{outputName}' not found in model. Available outputs:");
                    foreach (var name in outputNames)
                    {
                        Debug.LogWarning($" - {name}");
                    }
                }
                else
                {
                    // Try to peek at the output
                    Tensor outputTensor = worker.PeekOutput(outputName);
                    if (outputTensor != null)
                    {
                        // Fix: Access dimensions through shape property
                        int[] outputDims = outputTensor.shape.dimensions;
                        Debug.Log($"Output tensor shape: {string.Join(" × ", outputDims)}");
                        
                        // Calculate expected size
                        int totalOutputSize = 1;
                        foreach (int dim in outputDims)
                        {
                            totalOutputSize *= dim;
                        }
                        
                        Debug.Log($"Total output elements: {totalOutputSize}");
                        
                        if (totalOutputSize == 7593984) // This is the size from error message
                        {
                            Debug.Log("Current configuration produces the exact tensor size in error message!");
                            Debug.Log("This suggests the input configuration is correct, but the reshape operation is invalid.");
                        }
                    }
                }
                
                dummyInput.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error executing model: {e.Message}");
            }
            
            worker.Dispose();
            
            Debug.Log("========================================");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to analyze reshape error: {e.Message}");
        }
    }

    private bool IsNHWCFormat(string inputName, IEnumerable<string> layerNames)
    {
        // Check if the input name contains clues about the format
        if (inputName.Contains("NHWC"))
            return true;
        if (inputName.Contains("NCHW"))
            return false;

        // Most common format is NHWC for TensorFlow-derived models and NCHW for PyTorch
        // This is a heuristic and might need more sophisticated logic based on the model
        return true; // Default to NHWC as most common
    }

    [ContextMenu("Analyze Model Input")]
    public void AnalyzeModelInput()
    {
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager is not assigned!");
            return;
        }

        var model = segmentationManager.ModelAsset;
        if (model == null)
        {
            Debug.LogError("Model is not assigned in SegmentationManager!");
            return;
        }

        Model runtimeModel = null;
        IWorker worker = null;

        try
        {
            runtimeModel = ModelLoader.Load(model);
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
            
            Debug.Log("========== MODEL INPUT ANALYSIS ==========");
            
            // Get expected input shape from model
            if (runtimeModel.inputs.Count == 0)
            {
                Debug.LogError("Model has no inputs!");
                return;
            }
            
            // Fix: Use the name property of the input
            string inputName = runtimeModel.inputs[0].name;
            Debug.Log($"Model input name: {inputName}");
            
            // Create a test tensor with current settings to see if it works
            var currentWidth = segmentationManager.inputWidth;
            var currentHeight = segmentationManager.inputHeight;
            var currentChannels = segmentationManager.inputChannels;
            
            Debug.Log($"Current input settings: {currentWidth}x{currentHeight}x{currentChannels}");
            
            // Calculate total input elements
            int totalInputElements = currentWidth * currentHeight * currentChannels;
            Debug.Log($"Total input elements: {totalInputElements}");
            
            // Try to create tensor with current settings
            try
            {
                // Create tensor based on current format (NHWC or NCHW)
                Tensor inputTensor;
                if (segmentationManager.isModelNHWCFormat)
                {
                    inputTensor = new Tensor(1, currentHeight, currentWidth, currentChannels);
                    Debug.Log("Created NHWC tensor successfully");
                }
                else
                {
                    inputTensor = new Tensor(1, currentChannels, currentHeight, currentWidth);
                    Debug.Log("Created NCHW tensor successfully");
                }
                
                // Try to execute with this tensor
                Debug.Log("Attempting to execute model with current settings...");
                worker.Execute(inputTensor);
                
                // If we got here, the execution was successful
                Debug.Log("✅ Model executed successfully with current input shape!");
                
                // Try to peek at the output to see its shape
                var outputName = segmentationManager.outputName;
                try
                {
                    var output = worker.PeekOutput(outputName);
                    if (output != null)
                    {
                        Debug.Log($"Output '{outputName}' shape: {string.Join(" × ", output.shape.dimensions)}");
                        Debug.Log($"Total output elements: {output.shape.length}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error peeking at output '{outputName}': {e.Message}");
                    Debug.Log("Available outputs:");
                    foreach (var output in runtimeModel.outputs)
                    {
                        Debug.Log($"- {output}");
                    }
                }
                
                inputTensor.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error executing model with current settings: {e.Message}");
                
                // Try to suggest alternative dimensions
                Debug.Log("Trying to determine correct input dimensions...");
                
                // Common dimensions for ML models
                int[][] commonDimensions = new int[][]
                {
                    new int[] { 224, 224, 3 },  // MobileNet, EfficientNet
                    new int[] { 256, 256, 3 },  // Common segmentation models
                    new int[] { 299, 299, 3 },  // Inception
                    new int[] { 320, 320, 3 },  // Some DeepLab variants
                    new int[] { 512, 512, 3 },  // DeepLab
                    new int[] { 384, 384, 3 },  // DeepLab v3+
                    new int[] { 416, 416, 3 },  // YOLO
                    new int[] { 608, 608, 3 },  // YOLO variant
                    new int[] { 576, 256, 3 },  // Specifically for 72x32x103 output
                };
                
                Debug.Log("Testing common input dimensions...");
                bool foundMatch = false;
                
                foreach (var dims in commonDimensions)
                {
                    int w = dims[0];
                    int h = dims[1];
                    int c = dims[2];
                    
                    try
                    {
                        Tensor testTensor;
                        if (segmentationManager.isModelNHWCFormat)
                        {
                            testTensor = new Tensor(1, h, w, c);
                        }
                        else
                        {
                            testTensor = new Tensor(1, c, h, w);
                        }
                        
                        worker.Execute(testTensor);
                        Debug.Log($"✅ Model works with dimensions: {w}x{h}x{c}");
                        foundMatch = true;
                        
                        // Try to peek at the output
                        try
                        {
                            var outputName = segmentationManager.outputName;
                            var output = worker.PeekOutput(outputName);
                            if (output != null)
                            {
                                Debug.Log($"Output '{outputName}' shape: {string.Join(" × ", output.shape.dimensions)}");
                            }
                        }
                        catch { }
                        
                        testTensor.Dispose();
                        
                        // Only test a few options to save time
                        if (foundMatch) break;
                    }
                    catch
                    {
                        Debug.Log($"❌ Dimensions {w}x{h}x{c} do not work");
                    }
                }
                
                if (!foundMatch)
                {
                    Debug.LogError("Could not find working dimensions among common options");
                }
            }
            
            Debug.Log("========== END OF MODEL INPUT ANALYSIS ==========");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to analyze model input: {e.Message}");
        }
        finally
        {
            if (worker != null)
            {
                worker.Dispose();
            }
        }
    }

    public void AnalyzeReshapeError(int errorSize, int inputWidth, int inputHeight, int inputChannels, string outputName)
    {
        Debug.Log($"=== RESHAPE ERROR ANALYSIS ===");
        Debug.Log($"Error: Cannot reshape array of size {errorSize} into shape (n:1, h:32, w:72, c:103)");
        Debug.Log($"Expected output shape: (1, 32, 72, 103)");
        Debug.Log($"Expected elements: {1 * 32 * 72 * 103} = {1 * 32 * 72 * 103}");
        
        Debug.Log($"Input configuration: {inputWidth}x{inputHeight}x{inputChannels}");
        Debug.Log($"Output name: {outputName}");
        
        Debug.Log($"Array size: {errorSize}");
        
        // Analyze if this size makes sense for the given input dimensions
        int expectedOutputSize = inputWidth * inputHeight * 103;
        Debug.Log($"If output maintained the input resolution, we would expect: {inputWidth}x{inputHeight}x103 = {expectedOutputSize}");
        
        // Check for common scaling factors 
        float halfScale = 0.5f;
        int halfWidth = Mathf.RoundToInt(inputWidth * halfScale);
        int halfHeight = Mathf.RoundToInt(inputHeight * halfScale);
        int halfSizeEstimate = halfWidth * halfHeight * 103;
        Debug.Log($"If output is scaled to 50%: {halfWidth}x{halfHeight}x103 = {halfSizeEstimate}");
        
        float quarterScale = 0.25f;  
        int quarterWidth = Mathf.RoundToInt(inputWidth * quarterScale);
        int quarterHeight = Mathf.RoundToInt(inputHeight * quarterScale);
        int quarterSizeEstimate = quarterWidth * quarterHeight * 103;
        Debug.Log($"If output is scaled to 25%: {quarterWidth}x{quarterHeight}x103 = {quarterSizeEstimate}");
        
        float eighthScale = 0.125f;
        int eighthWidth = Mathf.RoundToInt(inputWidth * eighthScale);
        int eighthHeight = Mathf.RoundToInt(inputHeight * eighthScale);
        int eighthSizeEstimate = eighthWidth * eighthHeight * 103;
        Debug.Log($"If output is scaled to 12.5%: {eighthWidth}x{eighthHeight}x103 = {eighthSizeEstimate}");
        
        float thirdScale = 1f/3f;
        int thirdWidth = Mathf.RoundToInt(inputWidth * thirdScale);
        int thirdHeight = Mathf.RoundToInt(inputHeight * thirdScale);
        int thirdSizeEstimate = thirdWidth * thirdHeight * 103;
        Debug.Log($"If output is scaled to 33%: {thirdWidth}x{thirdHeight}x103 = {thirdSizeEstimate}");
        
        // Attempt to deduce dimensions from the provided size
        Debug.Log("Attempting to deduce dimensions from size...");
        
        // Check if it's a perfect square for width/height
        int pixelCount = errorSize / 103;
        float sqrtPixels = Mathf.Sqrt(pixelCount);
        bool isPerfectSquare = Mathf.Approximately(sqrtPixels, Mathf.Round(sqrtPixels));
        
        if (isPerfectSquare)
        {
            int dimension = Mathf.RoundToInt(sqrtPixels);
            Debug.Log($"Perfect square found: {dimension}x{dimension}x103 = {dimension * dimension * 103}");
        }
        else
        {
            Debug.Log("No perfect square dimensions found.");
            // Find factors that are close to a square
            for (int width = 1; width <= Mathf.Sqrt(pixelCount); width++)
            {
                if (pixelCount % width == 0)
                {
                    int height = pixelCount / width;
                    Debug.Log($"Possible dimensions: {width}x{height}x103 = {width * height * 103}");
                }
            }
        }
        
        Debug.Log("=== END OF ANALYSIS ===");
    }

    public void TryAutoFix(int errorSize)
    {
        Debug.Log("Attempting to auto-fix configuration based on error size...");
        
        if (segmentationManager == null)
        {
            Debug.LogError("No SegmentationManager assigned");
            return;
        }
        
        // Set output name to "logits" which is common for DeepLab models
        segmentationManager.outputName = "logits";
        
        // Try to find a configuration that would produce this error size
        bool found = false;
        
        // Calculate the number of pixels in the output (total size / number of classes)
        int numClasses = 103; // From the error message
        int numPixels = errorSize / numClasses;
        
        // Common input dimensions for DeepLab models
        int[][] dimensions = new int[][]
        {
            new int[] { 224, 224 }, // Common for many models
            new int[] { 256, 256 }, // Also common
            new int[] { 512, 512 }, // DeepLab v3
            new int[] { 513, 513 }, // DeepLab v3+
            new int[] { 321, 321 }, // DeepLab v2
            new int[] { 271, 271 }  // Some variants
        };
        
        // Common scaling factors between input and output
        float[] scalingFactors = new float[] { 1.0f, 0.5f, 0.25f, 0.125f, 1.0f/3.0f, 1.0f/8.0f };
        
        foreach (var dim in dimensions)
        {
            foreach (var scale in scalingFactors)
            {
                int outWidth = Mathf.RoundToInt(dim[0] * scale);
                int outHeight = Mathf.RoundToInt(dim[1] * scale);
                int size = outWidth * outHeight * numClasses;
                
                // Check if this configuration produces a size close to our error size
                if (Mathf.Abs(size - errorSize) < 100) // Allow small tolerance
                {
                    Debug.Log($"Found matching configuration: Input={dim[0]}x{dim[1]}, Output={outWidth}x{outHeight} (scale={scale})");
                    segmentationManager.inputWidth = dim[0];
                    segmentationManager.inputHeight = dim[1];
                    segmentationManager.inputChannels = 3;
                    found = true;
                    
                    // Apply the changes
                    segmentationManager.SetInputDimensions(dim[0], dim[1], 3);
                    break;
                }
            }
            
            if (found) break;
        }
        
        if (!found)
        {
            // If no exact match, check for factors of the output size
            Debug.Log("No exact configuration match found. Attempting to find a rectangular output shape...");
            
            for (int width = 1; width <= Mathf.Sqrt(numPixels); width++)
            {
                if (numPixels % width == 0)
                {
                    int height = numPixels / width;
                    Debug.Log($"Found output shape: {width}x{height}");
                    
                    // Find input dimensions that would produce this output shape
                    // Assume a 1:8 scaling (common for DeepLab)
                    float scale = 0.125f;
                    int inputWidth = Mathf.RoundToInt(width / scale);
                    int inputHeight = Mathf.RoundToInt(height / scale);
                    
                    Debug.Log($"Setting input dimensions to {inputWidth}x{inputHeight}x3 (1:8 scaling)");
                    segmentationManager.SetInputDimensions(inputWidth, inputHeight, 3);
                    found = true;
                    break;
                }
            }
        }
        
        if (!found)
        {
            Debug.LogError("Could not find a matching configuration for this error size");
        }
        else
        {
            Debug.Log("Auto-fix applied. Try running the scene again.");
        }
    }

    /// <summary>
    /// Examines the model's input and output tensors and logs their shapes without applying any changes.
    /// This is useful for debugging when you need to understand the model's structure.
    /// </summary>
    public void ExamineModel()
    {
        Debug.Log($"[ModelConfigFixer] Examining model tensor shapes for {modelAsset.name}");
        
        // Try to create the worker and setup
        Model runtimeModel = ModelLoader.Load(modelAsset);
        IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
        
        if (worker == null)
        {
            Debug.LogError("[ModelConfigFixer] Failed to create worker for model inspection");
            return;
        }
        
        try
        {
            // Log model details
            Debug.Log($"[ModelConfigFixer] Model: {modelAsset.name}");
            Debug.Log($"[ModelConfigFixer] ======= EXAMINING MODEL STRUCTURE =======");
            
            // Examine inputs
            Debug.Log($"[ModelConfigFixer] === INPUT TENSORS ===");
            Debug.Log($"[ModelConfigFixer] Number of input tensors: {runtimeModel.inputs.Count}");
            
            foreach (var input in runtimeModel.inputs)
            {
                Debug.Log($"[ModelConfigFixer] Input: {input.name}, Shape: [{string.Join(", ", input.shape)}]");
                
                // Additional information about the input if available
                Debug.Log($"[ModelConfigFixer] - Input dimensions: {string.Join(" × ", input.shape)}");
            }
            
            // Create dummy input to examine outputs
            // We'll use the first input as a guide
            if (runtimeModel.inputs.Count > 0)
            {
                var firstInput = runtimeModel.inputs[0];
                
                // Create a dummy tensor with the right shape
                Tensor dummyInput;
                
                // Check the shape info to determine format
                bool isNHWC = true; // Default to NHWC
                
                // Convert to int[] to avoid direct indexing issues
                int[] inputShape = new int[firstInput.shape.Length];
                for (int i = 0; i < firstInput.shape.Length; i++)
                {
                    inputShape[i] = firstInput.shape[i];
                }
                
                if (inputShape.Length >= 4)
                {
                    if (inputShape[3] == 3 || inputShape[3] == 1)
                    {
                        // Likely NHWC format (B,H,W,C)
                        dummyInput = new Tensor(inputShape[0], inputShape[1], inputShape[2], inputShape[3]);
                    }
                    else
                    {
                        // Likely NCHW format (B,C,H,W)
                        dummyInput = new Tensor(inputShape[0], inputShape[1], inputShape[2], inputShape[3]);
                        isNHWC = false;
                    }
                }
                else
                {
                    // If shape isn't clear, create a simple RGB input tensor in NHWC format
                    dummyInput = new Tensor(1, 224, 224, 3);
                }
                
                // Set the input and execute
                worker.Execute(dummyInput);
                dummyInput.Dispose();
            }
            else
            {
                // No inputs defined, can't execute model
                Debug.LogWarning("[ModelConfigFixer] Model has no defined inputs");
            }
            
            // Examine outputs
            Debug.Log($"[ModelConfigFixer] === OUTPUT TENSORS ===");
            Debug.Log($"[ModelConfigFixer] Number of output tensors: {runtimeModel.outputs.Count}");
            
            foreach (var outputName in runtimeModel.outputs)
            {
                Tensor outputTensor = worker.PeekOutput(outputName);
                if (outputTensor != null)
                {
                    Debug.Log($"[ModelConfigFixer] Output: {outputName}, Shape: [{string.Join(", ", outputTensor.shape.dimensions)}], Elements: {outputTensor.length}");
                    
                    // Additional information about the output
                    Debug.Log($"[ModelConfigFixer] - Batch (N): {outputTensor.shape.dimensions[0]}");
                    
                    // Try to determine if NCHW or NHWC format based on output tensor
                    int possibleClassCount;
                    
                    // Check if shape is compatible with NCHW format (class dimension is 1)
                    possibleClassCount = outputTensor.shape.dimensions[1];
                    Debug.Log($"[ModelConfigFixer] If NCHW format: Class count = {possibleClassCount}, Height = {outputTensor.shape.dimensions[2]}, Width = {outputTensor.shape.dimensions[3]}");
                    
                    // Check if shape is compatible with NHWC format (class dimension is 3)
                    if (outputTensor.shape.dimensions.Length >= 4)
                    {
                        possibleClassCount = outputTensor.shape.dimensions[3];
                        Debug.Log($"[ModelConfigFixer] If NHWC format: Height = {outputTensor.shape.dimensions[1]}, Width = {outputTensor.shape.dimensions[2]}, Class count = {possibleClassCount}");
                    }
                    
                    // Try to determine if this is a segmentation model by checking dimensions
                    // Segmentation models typically have output shape with class dimension that matches segmentation class count
                    if (segmentationManager != null)
                    {
                        int configuredClassCount = segmentationManager.segmentationClassCount;
                        if (possibleClassCount == configuredClassCount)
                        {
                            Debug.Log($"[ModelConfigFixer] Output shape is compatible with configured class count ({configuredClassCount})");
                        }
                        else
                        {
                            Debug.LogWarning($"[ModelConfigFixer] Output shape has {possibleClassCount} channels which does not match configured class count ({configuredClassCount})");
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[ModelConfigFixer] Output tensor '{outputName}' is null");
                }
            }
            
            // Suggest potential configuration options
            if (runtimeModel.outputs.Count > 0 && worker.PeekOutput(runtimeModel.outputs[0]) != null)
            {
                Tensor mainOutput = worker.PeekOutput(runtimeModel.outputs[0]);
                Debug.Log($"[ModelConfigFixer] === SUGGESTED CONFIGURATION ===");
                
                // Suggest input configuration
                if (runtimeModel.inputs.Count > 0)
                {
                    // Get input shape as an array to prevent direct indexing issues
                    int[] inputDims = new int[runtimeModel.inputs[0].shape.Length];
                    for (int i = 0; i < runtimeModel.inputs[0].shape.Length; i++)
                    {
                        inputDims[i] = runtimeModel.inputs[0].shape[i];
                    }
                    
                    if (inputDims.Length >= 4)
                    {
                        Debug.Log($"[ModelConfigFixer] Suggested input dimensions: {inputDims[2]}x{inputDims[1]}x{inputDims[3]} (if NHWC) or {inputDims[3]}x{inputDims[2]}x{inputDims[1]} (if NCHW)");
                    }
                }
                
                // Suggest possible output format (NCHW vs NHWC)
                if (mainOutput.shape.dimensions.Length == 4)
                {
                    if (mainOutput.shape.dimensions[1] > 32 && mainOutput.shape.dimensions[3] < 32)
                    {
                        Debug.Log($"[ModelConfigFixer] Based on dimensions, model likely uses NCHW format (Channels after Batch)");
                    }
                    else if (mainOutput.shape.dimensions[3] > 32 && mainOutput.shape.dimensions[1] < 32)
                    {
                        Debug.Log($"[ModelConfigFixer] Based on dimensions, model likely uses NHWC format (Channels after Width)");
                    }
                    else
                    {
                        Debug.Log($"[ModelConfigFixer] Unable to conclusively determine format from dimensions alone");
                    }
                }
            }
            
            Debug.Log($"[ModelConfigFixer] ======= MODEL EXAMINATION COMPLETE =======");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error examining model: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (worker != null)
            {
                worker.Dispose();
            }
        }
    }

    public void ExamineModelTensorShapes()
    {
        Debug.Log($"Examining model tensor shapes for {(segmentationManager != null ? segmentationManager.name : "null SegmentationManager")}");
        
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager reference is missing!");
            return;
        }

        if (segmentationManager.ModelAsset == null)
        {
            Debug.LogError("No model asset assigned to SegmentationManager!");
            return;
        }

        // Instead of calling methods directly, use reflection to safely call them if they exist
        var logInputShapeMethod = segmentationManager.GetType().GetMethod("LogModelInputShape", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (logInputShapeMethod != null)
        {
            logInputShapeMethod.Invoke(segmentationManager, null);
        }
        else
        {
            Debug.LogError("LogModelInputShape method not found in SegmentationManager");
        }
        
        var logOutputsMethod = segmentationManager.GetType().GetMethod("LogAvailableOutputs", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (logOutputsMethod != null)
        {
            logOutputsMethod.Invoke(segmentationManager, null);
        }
        else
        {
            Debug.LogError("LogAvailableOutputs method not found in SegmentationManager");
        }
        
        Debug.Log("Model examination complete.");
    }

    /// <summary>
    /// Analyzes possible tensor shapes for a given size and class count
    /// </summary>
    /// <param name="tensorSize">The size (number of elements) of the tensor</param>
    /// <param name="classCount">The number of classes expected in the segmentation</param>
    public void AnalyzeTensorShapePossibilities(long tensorSize, int classCount)
    {
        Debug.Log($"[TensorAnalysis] Analyzing possible tensor shapes for size {tensorSize} with {classCount} classes...");
        
        // Find factors of tensorSize/classCount to determine possible height/width pairs
        long pixelCount = tensorSize / classCount;
        bool isExactDivision = tensorSize % classCount == 0;
        
        Debug.Log($"[TensorAnalysis] Tensor has space for {pixelCount} pixels (tensorSize/classCount)");
        if (!isExactDivision) {
            Debug.LogWarning($"[TensorAnalysis] Warning: Tensor size {tensorSize} is not divisible by class count {classCount}. This suggests a format mismatch.");
        }
        
        // Find potential height/width pairs
        List<Vector2Int> possibleDimensions = new List<Vector2Int>();
        for (int i = 1; i <= Mathf.Sqrt(pixelCount); i++)
        {
            if (pixelCount % i == 0)
            {
                int height = i;
                int width = (int)(pixelCount / i);
                possibleDimensions.Add(new Vector2Int(width, height));
            }
        }
        
        if (possibleDimensions.Count > 0)
        {
            Debug.Log("[TensorAnalysis] Possible dimensions (width x height):");
            foreach (var dim in possibleDimensions)
            {
                float ratio = (float)dim.x / dim.y;
                Debug.Log($"[TensorAnalysis] - {dim.x} x {dim.y} (ratio: {ratio:F2})");
                
                // Suggest possible tensor shapes
                Debug.Log($"[TensorAnalysis]   NHWC format: [1, {dim.y}, {dim.x}, {classCount}]");
                Debug.Log($"[TensorAnalysis]   NCHW format: [1, {classCount}, {dim.y}, {dim.x}]");
            }
            
            // Try to find dimensions close to input dimensions from SegmentationManager
            if (segmentationManager != null)
            {
                float targetRatio = (float)segmentationManager.inputWidth / segmentationManager.inputHeight;
                Debug.Log($"[TensorAnalysis] Input dimensions ratio: {targetRatio:F2} ({segmentationManager.inputWidth}x{segmentationManager.inputHeight})");
                
                Vector2Int closestDim = possibleDimensions[0];
                float closestRatioDiff = Mathf.Abs((float)closestDim.x / closestDim.y - targetRatio);
                
                foreach (var dim in possibleDimensions)
                {
                    float ratioDiff = Mathf.Abs((float)dim.x / dim.y - targetRatio);
                    if (ratioDiff < closestRatioDiff)
                    {
                        closestDim = dim;
                        closestRatioDiff = ratioDiff;
                    }
                }
                
                Debug.Log($"[TensorAnalysis] Dimensions closest to input aspect ratio ({targetRatio:F2}): {closestDim.x} x {closestDim.y}");
                
                // Calculate the scaling factor from input dimensions
                float widthScale = (float)closestDim.x / segmentationManager.inputWidth;
                float heightScale = (float)closestDim.y / segmentationManager.inputHeight;
                Debug.Log($"[TensorAnalysis] Scale factor from input: width={widthScale:F3}x, height={heightScale:F3}x");
            }
            
            // Compare input size to output size
            Debug.Log($"[TensorAnalysis] Reference input size: 224x224x3 = {224*224*3} elements");
        }
        else
        {
            Debug.LogWarning("[TensorAnalysis] No possible dimensions found that divide evenly.");
        }
    }
} 