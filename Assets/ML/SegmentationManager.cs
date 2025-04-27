using UnityEngine;
using Unity.Barracuda;
using Unity.Barracuda.ONNX;
using System.Threading;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Experimental.Rendering;  // Add this for TextureCreationFlags
using System.Text;
using System.Linq; // Add this for Aggregate and other LINQ methods

#if UNITY_EDITOR
using System.IO;
#endif

/// <summary>
/// Manages ML model inference for wall segmentation.
/// Handles loading, running the model, and providing raw segmentation output.
/// </summary>
public class SegmentationManager : MonoBehaviour
{
    [Header("Model Configuration")]
    [SerializeField] public NNModel ModelAsset;
    [SerializeField] public string inputName = "images";
    [SerializeField] public string outputName = "output_segmentations";
    [SerializeField] public bool isModelNHWCFormat = true;
    [SerializeField] public int inputWidth = 513;
    [SerializeField] public int inputHeight = 513;
    [SerializeField] public int inputChannels = 3;
    [SerializeField] public int segmentationClassCount = 2; // Number of output classes (background + wall)
    [Range(0, 255)]
    [SerializeField] public int wallClassId = 1;
    [Range(0.0f, 1.0f)]
    [SerializeField] public float classificationThreshold = 0.5f;
    [SerializeField] private int processingInterval = 2;
    [SerializeField] private bool debugMode = false;
    [SerializeField] private WorkerFactory.Type _backend = WorkerFactory.Type.Auto;

    // Текстуры
    private Texture2D _inputTexture;
    private Texture2D _segmentationTexture;
    private RenderTexture _maskTexture;

    // Runtime модель и worker
    private Model _runtimeModel;
    private IWorker _worker;

    // Флаг обработки
    private bool _isProcessing = false;

    // Событие завершения обработки сегментации
    public Action<Texture2D> onProcessingComplete;
    
    // Событие, которое используется в других скриптах
    public event Action<Texture2D> OnSegmentationCompleted;

    private void Awake()
    {
        InitializeTextures();
        InitializeModel();
    }

    private void OnDisable()
    {
        // Make sure we dispose all resources when disabled
        DisposeWorker();
    }

    private void OnDestroy()
    {
        // Clean up worker
        DisposeWorker();
        
        // Clean up textures
        if (_inputTexture != null)
        {
            Destroy(_inputTexture);
            _inputTexture = null;
        }
        
        if (_segmentationTexture != null)
        {
            Destroy(_segmentationTexture);
            _segmentationTexture = null;
        }
        
        if (_maskTexture != null)
        {
            _maskTexture.Release();
            Destroy(_maskTexture);
            _maskTexture = null;
        }
    }

    private void InitializeTextures()
    {
        if (_inputTexture == null)
        {
            // Use GraphicsFormat for modern Unity versions
            _inputTexture = new Texture2D(
                inputWidth, 
                inputHeight, 
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
        }
        
        if (_segmentationTexture == null)
        {
            // Use GraphicsFormat for modern Unity versions
            _segmentationTexture = new Texture2D(
                inputWidth, 
                inputHeight, 
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
        }
        
        // Создаем маску в виде RenderTexture для дальнейшей обработки
        _maskTexture = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.R8);
        _maskTexture.Create();
    }

    public bool InitializeModel()
    {
        if (ModelAsset == null)
        {
            Debug.LogError("Model asset is not assigned");
            return false;
        }

        try
        {
            // Загружаем модель
            _runtimeModel = ModelLoader.Load(ModelAsset);
            
            if (_runtimeModel == null)
            {
                Debug.LogError("Failed to load the model");
                return false;
            }

            // Создаем worker для выполнения модели
            _worker = WorkerFactory.CreateWorker(_backend, _runtimeModel);
            
            if (debugMode)
            {
                Debug.Log($"Model loaded successfully: {ModelAsset.name}");
                LogTensorDimensions();
            }
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error initializing the model: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Определяет форму выходного тензора на основе метаданных модели
    /// </summary>
    private TensorShape DetermineOutputShape(Tensor output, TensorShape inputShape)
    {
        try
        {
            if (output == null)
            {
                Debug.LogError("Output tensor is null in DetermineOutputShape");
                return new TensorShape(0, 0, 0, 0);
            }
            
            if (debugMode)
            {
                // Safely handle output shape
                int[] outputDimensions = GetSafeDimensions(output.shape);
                Debug.Log($"Output tensor shape: dimensions={string.Join(",", outputDimensions)}");
            }
            
            // Get output dimensions safely
            int[] outDimensions = GetSafeDimensions(output.shape);
            
            if (outDimensions.Length >= 3)
            {
                return output.shape;
            }
            else
            {
                // If shape is too simple, try to infer from input dimensions
                Debug.LogWarning($"Output tensor has insufficient dimensions, attempting to reshape");
                
                // Fix: Convert input shape safely
                int[] inDimensions = GetSafeDimensions(inputShape);
                
                if (inDimensions.Length >= 4)
                {
                    // Determine if height and width are at index 1,2 (NCHW) or 1,2 (NHWC)
                    int height = inDimensions[1];
                    int width = inDimensions[2];
                    
                    // For DeepLab models, output is often [1, numClasses, height, width] or [1, height, width, numClasses]
                    if (outDimensions.Length > 0 && outDimensions[0] == 1)
                    {
                        if (output.length == height * width * segmentationClassCount)
                        {
                            // Shape is likely [1, H, W, C] or [1, C, H, W] but flattened
                            return new TensorShape(1, height, width, segmentationClassCount);
                        }
                    }
                }
            }
            
            Debug.LogError("Could not determine valid output shape");
            return new TensorShape(0, 0, 0, 0); // Return invalid shape to signal error
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in DetermineOutputShape: {e.Message}");
            return new TensorShape(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Safely retrieves dimensions from various shape types.
    /// </summary>
    /// <param name="shapeObj">The shape object, which can be TensorShape, int[], int, or Tensor</param>
    /// <returns>An array of dimensions, or an empty array if the shape cannot be determined</returns>
    private int[] GetSafeDimensions(object shapeObj)
    {
        if (shapeObj == null)
        {
            return new int[0];
        }
        
        try
        {
            if (shapeObj is TensorShape)
            {
                TensorShape tensorShape = (TensorShape)shapeObj;
                int[] result = new int[tensorShape.length];
                for (int i = 0; i < tensorShape.length; i++)
                {
                    result[i] = tensorShape[i];
                }
                return result;
            }
            else if (shapeObj is int[])
            {
                return (int[])shapeObj;
            }
            else if (shapeObj is int)
            {
                return new int[] { (int)shapeObj };
            }
            else if (shapeObj is Tensor)
            {
                Tensor tensor = (Tensor)shapeObj;
                if (tensor != null)
                {
                    TensorShape tensorShape = tensor.shape;
                    int[] result = new int[tensorShape.length];
                    for (int i = 0; i < tensorShape.length; i++)
                    {
                        result[i] = tensorShape[i];
                    }
                    return result;
                }
                return new int[0];
            }
            
            Debug.LogWarning($"Unsupported shape type: {shapeObj.GetType().Name}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in GetSafeDimensions: {ex.Message}");
        }
        
        return new int[0];
    }

    /// <summary>
    /// Обрабатывает текстуру камеры, выполняя сегментацию с помощью модели машинного обучения
    /// (Texture2D overload for compatibility with existing code)
    /// </summary>
    // Removing this duplicate method since we have a better implementation at line 1726
    /* 
    public bool ProcessCameraFrame(Texture2D cameraTexture, Vector2Int targetResolution = default)
    {
        if (targetResolution == default)
        {
            targetResolution = new Vector2Int(inputWidth, inputHeight);
        }
        
        if (!IsModelInitialized())
        {
            Debug.LogWarning("Cannot process camera frame - model is not initialized");
            return false;
        }

        if (_isProcessing)
        {
            if (debugMode)
            {
                Debug.Log("Skipping frame processing - already processing another frame");
            }
            return false;
        }

        _isProcessing = true;

        try
        {
            // Create input tensor directly from texture
            Tensor input = new Tensor(cameraTexture, inputChannels);
            
            // Execute the model with the input
            _worker.Execute(input);
            Tensor output = _worker.PeekOutput(_runtimeModel.outputs[0]);
            
            // Determine the output tensor shape from the model
            TensorShape outputShape = DetermineOutputShape(output, input.shape);
            
            // Get dimensions safely using GetSafeDimensions instead of directly accessing dimensions
            int[] shapeDims = GetSafeDimensions(outputShape);
            if (shapeDims.Length == 0 || shapeDims[0] == 0)
            {
                Debug.LogError("Failed to determine valid output shape");
                input.Dispose();
                _isProcessing = false;
                return false;
            }
            
            // Convert tensor to texture with wall class highlighted
            bool success = ConvertTensorToTexture(output, outputShape);
            
            // Clean up resources
            input.Dispose();
            
            _isProcessing = false;
            return success;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ProcessCameraFrame: {e.Message}\n{e.StackTrace}");
            _isProcessing = false;
            return false;
        }
    }
    */
    
    /// <summary>
    /// Resize source texture to target dimensions
    /// </summary>
    /// <returns>Resized texture</returns>
    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        
        // Create a new texture with the target dimensions using modern constructor
        Texture2D result = new Texture2D(
            targetWidth, 
            targetHeight, 
            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
            UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
        
        // Create a temporary RenderTexture for scaling
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
        
        // Copy source to the temporary RenderTexture
        Graphics.Blit(source, rt);
        
        // Store the active RenderTexture
        RenderTexture prev = RenderTexture.active;
        
        // Set the temporary RenderTexture as active
        RenderTexture.active = rt;
        
        // Read pixels from the active RenderTexture to the result texture
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        
        // Restore the previous active RenderTexture
        RenderTexture.active = prev;
        
        // Release the temporary RenderTexture
        RenderTexture.ReleaseTemporary(rt);
        
        return result;
    }
    
    /// <summary>
    /// Convert tensor output to single-channel texture with wall class
    /// </summary>
    private bool ConvertTensorToTexture(Tensor output, TensorShape shape)
    {
        try
        {
            // Get dimensions safely
            int[] shapeDims = GetSafeDimensions(shape);
            
            if (shapeDims.Length < 3)
            {
                Debug.LogError("Invalid tensor shape for conversion to texture");
                return false;
            }
            
            // Extract dimensions based on format
            int height, width, channelCount;
            
            if (isModelNHWCFormat)
            {
                // NHWC: [N, H, W, C]
                height = shapeDims[1];
                width = shapeDims[2];
                channelCount = shapeDims.Length > 3 ? shapeDims[3] : 1;
            }
            else
            {
                // NCHW: [N, C, H, W]
                channelCount = shapeDims[1];
                height = shapeDims[2];
                width = shapeDims[3];
            }
            
            // Create or resize segmentation texture if needed
            if (_segmentationTexture == null || 
                _segmentationTexture.width != width || 
                _segmentationTexture.height != height)
            {
                if (_segmentationTexture != null)
                {
                    Destroy(_segmentationTexture);
                }
                
                #if UNITY_2022_1_OR_NEWER
                // For newer Unity versions (2022.1+)
                _segmentationTexture = new Texture2D(
                    width, 
                    height, 
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                #else
                // For older Unity versions
                _segmentationTexture = new Texture2D(
                    width, 
                    height, 
                    TextureFormat.R8, 
                    false);
                #endif
            }
            
            // Create pixel data array
            Color32[] pixelData = new Color32[width * height];
            
            // Process each pixel
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // Get the classification value for wall class using safe method
                    float classValue = GetValueFromTensor(output, 0, y, x, wallClassId);
                    
                    // Apply threshold for classification (0 or 255)
                    byte intensity = classValue >= classificationThreshold ? (byte)255 : (byte)0;
                    
                    // Set pixel data
                    pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
                }
            }
            
            // Apply pixel data to texture
            _segmentationTexture.SetPixels32(pixelData);
            _segmentationTexture.Apply();
            
            // Invoke the completion callback
            onProcessingComplete?.Invoke(_segmentationTexture);
            OnSegmentationCompleted?.Invoke(_segmentationTexture);
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ConvertTensorToTexture: {e.Message}");
            return false;
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
    
    private void LogTensorDimensions()
    {
        if (!IsModelInitialized())
        {
            Debug.LogError("Cannot log tensor dimensions - model is not initialized");
            return;
        }

        Debug.Log("Model input and output tensors:");
        
        // Log inputs
        Debug.Log($"Input count: {_runtimeModel.inputs.Count}");
        foreach (var input in _runtimeModel.inputs)
        {
            Debug.Log($"Input: name='{input.name}', shape={string.Join(",", input.shape)}");
        }
        
        // Log outputs
        Debug.Log($"Output count: {_runtimeModel.outputs.Count}");
        for (int i = 0; i < _runtimeModel.outputs.Count; i++)
        {
            Debug.Log($"Output {i}: name='{_runtimeModel.outputs[i]}'");
            // Note: Can't access output shape without executing the model
        }
        
        // Fix: Handle int to int[] conversion for dimensions
        var inShape = _worker.PeekOutput(inputName).shape;
        int[] inputDims = GetSafeDimensions(inShape);
        
        // Log configuration
        Debug.Log($"Input dimensions: {inputWidth}x{inputHeight}x{inputChannels}, NHWC format: {isModelNHWCFormat}");
        Debug.Log($"Output: name='{outputName}', class count={segmentationClassCount}, wall class={wallClassId}");
    }

    // Add new method to log model input shape
    public void LogModelInputShape()
    {
        if (_runtimeModel == null || _worker == null)
        {
            Debug.LogError("Model not initialized");
            return;
        }
        
        Debug.Log("=== MODEL INPUT SHAPE ===");
        
        foreach (var input in _runtimeModel.inputs)
        {
            // Safely get dimensions
            int[] shapeDims = GetSafeDimensions(input.shape);
            string dimString = string.Join(", ", shapeDims);
            Debug.Log($"Input {input.name}: shape = [{dimString}]");
        }
        
        Debug.Log("========================");
    }

    // Add method to log available outputs
    public void LogAvailableOutputs()
    {
        if (!IsModelInitialized())
        {
            Debug.LogError("Cannot log available outputs - model is not initialized");
            return;
        }
        
        Debug.Log("=== AVAILABLE OUTPUTS ===");
        
        // Create a dummy tensor with the currently configured dimensions
        Tensor dummyInput = null;
        
        try
        {
            if (isModelNHWCFormat)
            {
                dummyInput = new Tensor(1, inputHeight, inputWidth, inputChannels);
            }
            else
            {
                dummyInput = new Tensor(1, inputChannels, inputHeight, inputWidth);
            }
            
            // Execute model with dummy input to populate outputs
            _worker.Execute(dummyInput);
            
            // Log the available outputs
            Debug.Log($"Output count: {_runtimeModel.outputs.Count}");
            
            for (int i = 0; i < _runtimeModel.outputs.Count; i++)
            {
                string outputNameI = _runtimeModel.outputs[i];
                Debug.Log($"Output {i}: {outputNameI}");
                
                try
                {
                    Tensor outputTensor = _worker.PeekOutput(outputNameI);
                    if (outputTensor != null)
                    {
                        int[] outputDims = GetSafeDimensions(outputTensor.shape);
                        Debug.Log($"  Shape: {string.Join("×", outputDims)}");
                        Debug.Log($"  Length: {outputTensor.length}");
                    }
                    else
                    {
                        Debug.LogWarning($"  Could not peek output tensor: {outputNameI}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"  Error peeking output tensor: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error logging available outputs: {e.Message}");
        }
        finally
        {
            if (dummyInput != null)
            {
                dummyInput.Dispose();
            }
        }
        
        Debug.Log("========================");
    }

    // Структура для хранения информации о форме тензора
    private struct TensorShapeInfo
    {
        public int batch;
        public int height;
        public int width;
        public int channels;
    }

    /// <summary>
    /// Подготавливает входной тензор из кадра камеры с заданным разрешением
    /// </summary>
    private Tensor PrepareInputTensorFromFrame(XRCameraFrame frame, Vector2Int targetResolution)
    {
        if (frame.timestampNs == 0)
        {
            Debug.LogWarning("Invalid camera frame: timestamp is 0");
            return null;
        }
        
        // Создаем или обновляем текстуру для входных данных
        if (_inputTexture == null || 
            _inputTexture.width != targetResolution.x || 
            _inputTexture.height != targetResolution.y)
        {
            if (_inputTexture != null)
            {
                Destroy(_inputTexture);
            }
            
            _inputTexture = new Texture2D(
                targetResolution.x,
                targetResolution.y,
                TextureFormat.RGBA32,
                false);
        }
        
        try
        {
            // Since TryGetCameraImage is not available, create a texture directly from the camera frame
            // This is a simplified approach - you may need custom code to extract image data from your specific camera frame format
            
            // Get camera texture (this will depend on your AR implementation)
            Texture2D cameraTexture = new Texture2D(2, 2);
            
            // For demo purposes, create a simple texture
            // In a real application, you would get this from the camera/AR system
            Color[] pixels = new Color[targetResolution.x * targetResolution.y];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.black;
            }
            
            _inputTexture.SetPixels(pixels);
            _inputTexture.Apply();
            
            // Create input tensor from the texture
            return new Tensor(_inputTexture, inputChannels);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing input tensor: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Обрабатывает результат сегментации и вызывает события при обнаружении стен
    /// </summary>
    private void ProcessSegmentationMask()
    {
        if (_segmentationTexture == null)
        {
            Debug.LogWarning("Cannot process segmentation mask: texture is null");
            return;
        }
        
        // Здесь можно добавить дополнительную обработку маски сегментации
        // Например, постобработку, фильтрацию шума и т.д.
        
        // Уведомить о завершении обработки
        onProcessingComplete?.Invoke(_segmentationTexture);
        OnSegmentationCompleted?.Invoke(_segmentationTexture);
    }

    private bool IsModelInitialized()
    {
        return _runtimeModel != null && _worker != null;
    }

    private Tensor PrepareInputTensor(XRCpuImage image, Vector2Int targetDims, bool normalizeInput = true)
    {
        // Check only standard formats that exist in all Unity versions
        if (image.format != XRCpuImage.Format.AndroidYuv420_888)
        {
            Debug.LogWarning($"Potentially unsupported image format: {image.format}, will attempt to convert anyway");
            // Continue processing - we'll let the conversion try to handle it
        }

        try
        {
            // Configure conversion parameters
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(targetDims.x, targetDims.y),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            // Calculate buffer size and allocate
            int bufferSize = image.GetConvertedDataSize(conversionParams);
            var buffer = new Unity.Collections.NativeArray<byte>(bufferSize, Unity.Collections.Allocator.Temp);

            // Convert the image to RGBA
            image.Convert(conversionParams, buffer);

            // Create a texture from the buffer
            if (_inputTexture == null || _inputTexture.width != targetDims.x || _inputTexture.height != targetDims.y)
            {
                if (_inputTexture != null) 
                    Destroy(_inputTexture);
                
                #if UNITY_2022_1_OR_NEWER
                _inputTexture = new Texture2D(
                    targetDims.x, 
                    targetDims.y, 
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                #else
                _inputTexture = new Texture2D(targetDims.x, targetDims.y, TextureFormat.RGBA32, false);
                #endif
            }
            
            _inputTexture.LoadRawTextureData(buffer);
            _inputTexture.Apply();

            // Dispose the temporary buffer
            buffer.Dispose();

            // Determine input tensor format based on model
            int batchSize = 1;
            int height = targetDims.y;
            int width = targetDims.x;
            int channels = 3; // RGB channels
            
            // Find expected input shape from model
            if (_runtimeModel != null && _runtimeModel.inputs.Count > 0)
            {
                var modelInputShape = _runtimeModel.inputs[0].shape;
                if (debugMode)
                {
                    Debug.Log($"Model input shape: {string.Join("x", modelInputShape)}");
                }
                
                // Check if we have NCHW or NHWC format by looking at the last dimension
                // Most models use NHWC (batch, height, width, channels) format where the 
                // 4th dimension (index 3) is the channel count
                if (modelInputShape.Length >= 4)
                {
                    // Get shape as array for safe indexing
                    int[] shapeArray = new int[modelInputShape.Length];
                    for (int i = 0; i < modelInputShape.Length; i++)
                    {
                        shapeArray[i] = modelInputShape[i];
                    }
                    
                    // Different models have different input formats (NCHW vs NHWC)
                    // We'll need to adjust accordingly - try to detect which format
                    channels = shapeArray[3] == 3 ? 3 : shapeArray[1];
                }
            }

            // Create input tensor of appropriate shape
            Tensor inputTensor = new Tensor(new TensorShape(batchSize, height, width, channels));

            // Sample pixels from the texture and fill the tensor
            Color32[] pixels = _inputTexture.GetPixels32();
            
            // Fill the tensor with RGB values from the texture
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    Color32 pixel = pixels[pixelIndex];
                    
                    // For RGB input, we need 3 channels
                    if (channels == 3)
                    {
                        // Normalize if requested (convert 0-255 to 0-1 or other normalization)
                        float r = normalizeInput ? pixel.r / 255.0f : pixel.r;
                        float g = normalizeInput ? pixel.g / 255.0f : pixel.g;
                        float b = normalizeInput ? pixel.b / 255.0f : pixel.b;
                        
                        // Set the tensor values based on expected format
                        inputTensor[0, y, x, 0] = r;
                        inputTensor[0, y, x, 1] = g;
                        inputTensor[0, y, x, 2] = b;
                    }
                    // Some models require single channel (grayscale)
                    else if (channels == 1)
                    {
                        // Convert to grayscale
                        float gray = (pixel.r + pixel.g + pixel.b) / 3.0f;
                        gray = normalizeInput ? gray / 255.0f : gray;
                        
                        inputTensor[0, y, x, 0] = gray;
                    }
                }
            }
            
            if (debugMode)
            {
                Debug.Log($"Created input tensor with shape: {inputTensor.shape}");
            }
            
            return inputTensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing input tensor: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Обрабатывает кадр камеры, выполняя сегментацию с помощью модели машинного обучения
    /// (XRCameraFrame version)
    /// </summary>
    public bool ProcessCameraFrame(XRCameraFrame frame, Vector2Int targetResolution = default)
    {
        if (targetResolution == default)
        {
            targetResolution = new Vector2Int(inputWidth, inputHeight);
        }
        
        if (!IsModelInitialized())
        {
            Debug.LogWarning("Cannot process camera frame - model is not initialized");
            return false;
        }

        if (_isProcessing)
        {
            if (debugMode)
            {
                Debug.Log("Skipping frame processing - already processing another frame");
            }
            return false;
        }

        _isProcessing = true;

        try
        {
            // Create a simple texture as we can't directly use XRCameraFrame 
            // This is just a placeholder - in a real implementation, you'd extract the image data from the frame
            Texture2D cameraTexture = new Texture2D(targetResolution.x, targetResolution.y, TextureFormat.RGBA32, false);
            
            // Fill with placeholder data (black texture)
            Color[] pixels = new Color[targetResolution.x * targetResolution.y];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.black;
            }
            cameraTexture.SetPixels(pixels);
            cameraTexture.Apply();
            
            // Use the texture overload to process the frame
            bool result = ProcessCameraFrame(cameraTexture, targetResolution);
            
            // Clean up the temporary texture
            Destroy(cameraTexture);
            
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ProcessCameraFrame with XRCameraFrame: {e.Message}\n{e.StackTrace}");
            _isProcessing = false;
            return false;
        }
    }

    /// <summary>
    /// Process a camera frame and run the segmentation model on it
    /// </summary>
    /// <param name="image">The camera image to process</param>
    public void ProcessCameraFrame(XRCpuImage image)
    {
        if (!IsModelInitialized())
        {
            Debug.LogError("Model is not initialized.");
            return;
        }

        try
        {
            // Create input tensor from the image
            Tensor inputTensor = null;
            Tensor outputTensor = null;
            
            try
            {
                inputTensor = PrepareInputTensorFromXRCpuImage(image);
                
                if (inputTensor == null)
                {
                    Debug.LogError("Failed to prepare input tensor from camera frame.");
                    return;
                }

                if (!ValidateInputDimensions(inputTensor))
                {
                    Debug.LogWarning("Input tensor dimensions don't match configuration. Attempting to continue anyway.");
                }

                // Execute the model with the input tensor
                _worker.Execute(inputTensor);

                // Retrieve the output tensor
                outputTensor = _worker.PeekOutput(outputName) as Tensor;

                if (outputTensor == null)
                {
                    Debug.LogError($"Failed to retrieve output tensor '{outputName}'");
                    return;
                }

                // Create a valid input shape for DetermineOutputShape
                var inputShape = new TensorShape(1, inputHeight, inputWidth, inputChannels);
                TensorShape outputShape = DetermineOutputShape(outputTensor, inputShape);
                
                // Convert the output tensor to a texture
                bool success = ConvertTensorToTexture(outputTensor, outputShape);
                
                if (!success)
                {
                    Debug.LogError("Failed to convert output tensor to texture");
                }
            }
            catch (Exception e)
            {
                if (outputTensor != null)
                {
                    HandleTensorReshapeError(outputTensor, e, new int[] { inputHeight, inputWidth, inputChannels });
                }
                else
                {
                    Debug.LogError($"Error during model execution: {e.Message}");
                }
            }
            finally
            {
                // Clean up tensor objects
                if (inputTensor != null)
                {
                    inputTensor.Dispose();
                    inputTensor = null;
                }
                
                if (outputTensor != null)
                {
                    outputTensor.Dispose();
                    outputTensor = null;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ProcessCameraFrame: {e.Message}");
        }
    }

    // Helper method to prepare input tensor from XRCpuImage
    private Tensor PrepareInputTensorFromXRCpuImage(XRCpuImage image)
    {
        Vector2Int targetDims = new Vector2Int(inputWidth, inputHeight);
        
        // Check only standard formats that exist in all Unity versions
        if (image.format != XRCpuImage.Format.AndroidYuv420_888)
        {
            Debug.LogWarning($"Potentially unsupported image format: {image.format}, will attempt to convert anyway");
            // Continue processing - we'll let the conversion try to handle it
        }

        try
        {
            // Configure conversion parameters
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = targetDims,
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            // Calculate buffer size and allocate
            int bufferSize = image.GetConvertedDataSize(conversionParams);
            var buffer = new Unity.Collections.NativeArray<byte>(bufferSize, Unity.Collections.Allocator.Temp);

            // Convert the image to RGBA
            image.Convert(conversionParams, buffer);

            // Create a texture from the buffer
            if (_inputTexture == null || _inputTexture.width != targetDims.x || _inputTexture.height != targetDims.y)
            {
                if (_inputTexture != null) 
                    Destroy(_inputTexture);
                
                #if UNITY_2022_1_OR_NEWER
                _inputTexture = new Texture2D(
                    targetDims.x, 
                    targetDims.y, 
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                #else
                _inputTexture = new Texture2D(targetDims.x, targetDims.y, TextureFormat.RGBA32, false);
                #endif
            }
            
            _inputTexture.LoadRawTextureData(buffer);
            _inputTexture.Apply();

            // Dispose the temporary buffer
            buffer.Dispose();

            // Create input tensor of appropriate shape
            Tensor inputTensor;
            
            if (isModelNHWCFormat)
            {
                // NHWC format: [batch, height, width, channels]
                inputTensor = new Tensor(1, inputHeight, inputWidth, inputChannels);
            }
            else
            {
                // NCHW format: [batch, channels, height, width]
                inputTensor = new Tensor(1, inputChannels, inputHeight, inputWidth);
            }

            // Sample pixels from the texture and fill the tensor
            Color32[] pixels = _inputTexture.GetPixels32();
            
            // Fill the tensor with RGB values from the texture
            for (int y = 0; y < inputHeight; y++)
            {
                for (int x = 0; x < inputWidth; x++)
                {
                    int pixelIndex = y * inputWidth + x;
                    Color32 pixel = pixels[pixelIndex];
                    
                    // For RGB input, we need 3 channels
                    float r = pixel.r / 255.0f;
                    float g = pixel.g / 255.0f;
                    float b = pixel.b / 255.0f;
                    
                    // Set the tensor values based on expected format
                    if (isModelNHWCFormat)
                    {
                        inputTensor[0, y, x, 0] = r;
                        inputTensor[0, y, x, 1] = g;
                        inputTensor[0, y, x, 2] = b;
                    }
                    else
                    {
                        inputTensor[0, 0, y, x] = r;
                        inputTensor[0, 1, y, x] = g;
                        inputTensor[0, 2, y, x] = b;
                    }
                }
            }
            
            return inputTensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing input tensor: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    // Add setter for input dimensions
    public void SetInputDimensions(int width, int height, int channels)
    {
        inputWidth = width;
        inputHeight = height;
        inputChannels = channels;
        
        Debug.Log($"Set input dimensions to: {width}x{height}x{channels}");
        
        // Setup new input shape for model
        int[] newInputShape = new[] { 1, height, width, channels };
        
        // Reinitialize textures with new dimensions
        if (_inputTexture != null)
        {
            Destroy(_inputTexture);
            #if UNITY_2022_1_OR_NEWER
            _inputTexture = new Texture2D(
                width, 
                height, 
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            #else
            _inputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            #endif
        }
        
        if (_segmentationTexture != null)
        {
            Destroy(_segmentationTexture);
            #if UNITY_2022_1_OR_NEWER
            _segmentationTexture = new Texture2D(
                width, 
                height, 
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            #else
            _segmentationTexture = new Texture2D(width, height, TextureFormat.R8, false);
            #endif
        }
    }

    // Add method to examine model tensor shapes
    public void ExamineModelTensorShapes()
    {
        if (!IsModelInitialized())
        {
            Debug.LogError("Model is not initialized, cannot examine tensor shapes");
            return;
        }
        
        Debug.Log("===== EXAMINING MODEL TENSOR SHAPES =====");
        
        // Log model inputs
        Debug.Log("MODEL INPUTS:");
        foreach (var input in _runtimeModel.inputs)
        {
            Debug.Log($"- Input: {input.name}");
        }
        
        // Log model outputs
        Debug.Log("MODEL OUTPUTS:");
        foreach (var output in _runtimeModel.outputs)
        {
            Debug.Log($"- Output: {output}");
        }
        
        // Try to get actual output shape by running a dummy tensor
        try
        {
            // Create a dummy tensor with the currently configured dimensions
            Tensor dummyInput;
            
            if (isModelNHWCFormat)
            {
                dummyInput = new Tensor(1, inputHeight, inputWidth, inputChannels);
            }
            else
            {
                dummyInput = new Tensor(1, inputChannels, inputHeight, inputWidth);
            }
            
            // Fill with zeros
            for (int i = 0; i < dummyInput.length; i++)
            {
                dummyInput[i] = 0f;
            }
            
            // Execute model with dummy tensor
            _worker.Execute(dummyInput);
            
            // Check output tensor shape
            Tensor outputTensor = _worker.PeekOutput(outputName) as Tensor;
            if (outputTensor != null)
            {
                // Use GetSafeDimensions instead of direct access to shape.dimensions
                int[] outputDims = GetSafeDimensions(outputTensor.shape);
                
                Debug.Log($"Output tensor shape: {string.Join(" × ", outputDims)}");
                Debug.Log($"Output tensor length: {outputTensor.length}");
                
                // Calculate total elements
                int totalElements = 1;
                foreach (int dim in outputDims)
                {
                    totalElements *= dim;
                }
                
                Debug.Log($"Total elements in output tensor: {totalElements}");
            }
            else
            {
                Debug.LogError("Could not get output tensor shape - output tensor is null");
            }
            
            // Clean up
            dummyInput.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error examining tensor shapes: {e.Message}");
        }
        
        Debug.Log("======================================");
    }

    private string GetOutputTensorName()
    {
        return outputName;
    }

    /// <summary>
    /// Handles errors related to tensor reshaping by providing detailed diagnostic information
    /// </summary>
    private void HandleTensorReshapeError(Tensor tensor, Exception error, int[] expectedDimensions)
    {
        // Get tensor details if available
        string tensorInfo = "Unknown tensor dimensions";
        long tensorSize = 0;
        
        if (tensor != null)
        {
            tensorSize = tensor.length;
            tensorInfo = $"Tensor dimensions: n:{tensor.batch}, h:{tensor.height}, w:{tensor.width}, c:{tensor.channels}";
        }
        
        // Calculate expected total size
        long expectedSize = 1;
        foreach (int dim in expectedDimensions)
        {
            expectedSize *= dim;
        }
        
        // Log detailed diagnostic information
        Debug.LogError($"Tensor reshape error: {error.Message}\n" +
                     $"Tensor size: {tensorSize}, Expected total: {expectedSize}\n" +
                     $"Expected dimensions: n:1, h:{expectedDimensions[0]}, w:{expectedDimensions[1]}, c:{expectedDimensions[2]}\n" +
                     $"{tensorInfo}\n" +
                     $"This is likely due to a mismatch between the model's output shape and what the code is expecting.");
                 
        // Suggested fixes
        Debug.LogWarning($"Try adjusting the input dimensions in SegmentationManager: inputWidth={inputWidth}, inputHeight={inputHeight}, " +
                       $"or ensure the model's expected input dimensions match these values.");
    }

    /// <summary>
    /// Validates input tensor dimensions against configured values
    /// </summary>
    private bool ValidateInputDimensions(Tensor inputTensor)
    {
        if (inputTensor == null) return false;
        
        bool dimensionsMatch = inputTensor.height == inputHeight && 
                              inputTensor.width == inputWidth && 
                              inputTensor.channels == inputChannels;
                          
        if (!dimensionsMatch && debugMode)
        {
            Debug.LogWarning($"Input tensor dimensions (n:{inputTensor.batch}, h:{inputTensor.height}, " +
                           $"w:{inputTensor.width}, c:{inputTensor.channels}) don't match expected " +
                           $"dimensions (n:1, h:{inputHeight}, w:{inputWidth}, c:{inputChannels})");
        }
        
        return dimensionsMatch;
    }

    /// <summary>
    /// Prepare input tensor from Texture2D
    /// </summary>
    /// <param name="texture">Input texture</param>
    /// <returns>Input tensor ready for inference</returns>
    public Tensor PrepareInputTensorFromTexture(Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogError("Input texture is null");
            return null;
        }
        
        try
        {
            // Resize texture if needed
            Texture2D processTexture = null;
            
            if (texture.width != inputWidth || texture.height != inputHeight)
            {
                processTexture = ResizeTexture(texture, inputWidth, inputHeight);
            }
            else
            {
                processTexture = texture;
            }
            
            // Convert texture to tensor
            return CreateInputTensorFromTexture(processTexture);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing input tensor from texture: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Process a texture for segmentation
    /// </summary>
    /// <param name="texture">Texture to process</param>
    /// <returns>Success status of the processing</returns>
    public bool ProcessTexture(Texture2D texture)
    {
        if (!IsModelInitialized())
        {
            Debug.LogWarning("Cannot process texture - model is not initialized");
            return false;
        }

        if (_isProcessing)
        {
            if (debugMode)
            {
                Debug.Log("Skipping texture processing - already processing another frame");
            }
            return false;
        }

        _isProcessing = true;

        try
        {
            // Prepare input tensor from texture
            Tensor input = PrepareInputTensorFromTexture(texture);
            
            if (input == null)
            {
                Debug.LogError("Failed to prepare input tensor from texture");
                _isProcessing = false;
                return false;
            }
            
            // Execute the model with the input
            _worker.Execute(input);
            Tensor output = _worker.PeekOutput(outputName) as Tensor;
            
            if (output == null)
            {
                Debug.LogError("Failed to get output tensor from model");
                input.Dispose();
                _isProcessing = false;
                return false;
            }
            
            // Determine the output tensor shape from the model
            TensorShape outputShape = DetermineOutputShape(output, input.shape);
            
            // Get dimensions safely using GetSafeDimensions instead of directly accessing dimensions
            int[] shapeDims = GetSafeDimensions(outputShape);
            if (shapeDims.Length == 0 || shapeDims[0] == 0)
            {
                Debug.LogError("Failed to determine valid output shape");
                input.Dispose();
                _isProcessing = false;
                return false;
            }
            
            // Convert tensor to texture with wall class highlighted
            bool success = ConvertTensorToTexture(output, outputShape);
            
            // Clean up resources
            input.Dispose();
            
            _isProcessing = false;
            return success;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ProcessTexture: {e.Message}\n{e.StackTrace}");
            _isProcessing = false;
            return false;
        }
    }

    /// <summary>
    /// Log possible tensor shapes for a given total number of elements
    /// This helps diagnose model shape compatibility issues
    /// </summary>
    public void LogPossibleTensorShapes(int totalElements, int maxClassCount = 200)
    {
        Debug.Log($"=== Analyzing possible shapes for tensor with {totalElements} elements ===");
        
        // Check square dimensions with various class counts
        Debug.Log("Checking square dimensions with varying class counts:");
        for (int classes = 1; classes <= maxClassCount; classes++)
        {
            double pixelCount = (double)totalElements / classes;
            double squareSide = Math.Sqrt(pixelCount);
            
            // Check if we have a close to integer square root
            if (Math.Abs(squareSide - Math.Round(squareSide)) < 0.001)
            {
                int side = (int)Math.Round(squareSide);
                Debug.Log($"- Classes: {classes}, Dimensions: {side}x{side}");
                Debug.Log($"  NHWC: [1, {side}, {side}, {classes}] = {1 * side * side * classes} elements");
                Debug.Log($"  NCHW: [1, {classes}, {side}, {side}] = {1 * classes * side * side} elements");
            }
        }
        
        // Check non-square dimensions with common aspect ratios for classes we found above
        Debug.Log("\nChecking non-square dimensions with common aspect ratios:");
        int[] commonClassCounts = { 1, 2, 3, 21, 80, 91, 103, 171 }; // Add your expected class counts here
        float[] aspectRatios = { 4f/3f, 16f/9f, 2f, 3f/2f }; // Common aspect ratios
        
        // Using foreach correctly with arrays
        foreach (int classes in commonClassCounts)
        {
            double pixelCount = (double)totalElements / classes;
            double squareSide = Math.Sqrt(pixelCount);
            int approxSide = (int)Math.Round(squareSide);
            
            // Only proceed if this is close to a valid class count
            if (Math.Abs(squareSide - approxSide) > 0.1)
                continue;
                
            Debug.Log($"\nFor {classes} classes ({pixelCount} pixels):");
            
            // Check non-square dimensions
            foreach (float ratio in aspectRatios)
            {
                // Calculate width and height based on aspect ratio
                // width/height = ratio, and width*height = pixelCount
                // So width = ratio*height, and ratio*height*height = pixelCount
                // Therefore height = sqrt(pixelCount/ratio)
                
                double height = Math.Sqrt(pixelCount / ratio);
                double width = height * ratio;
                
                // Only show if both dimensions are close to integers
                if (Math.Abs(height - Math.Round(height)) < 0.1 && 
                    Math.Abs(width - Math.Round(width)) < 0.1)
                {
                    int h = (int)Math.Round(height);
                    int w = (int)Math.Round(width);
                    
                    Debug.Log($"- Aspect ratio {ratio}: {w}x{h}");
                    Debug.Log($"  NHWC: [1, {h}, {w}, {classes}] = {1 * h * w * classes} elements");
                    Debug.Log($"  NCHW: [1, {classes}, {h}, {w}] = {1 * classes * h * w} elements");
                    
                    // Check the reverse as well (swapped width and height)
                    Debug.Log($"- Aspect ratio {1/ratio}: {h}x{w}");
                    Debug.Log($"  NHWC: [1, {w}, {h}, {classes}] = {1 * w * h * classes} elements");
                    Debug.Log($"  NCHW: [1, {classes}, {w}, {h}] = {1 * classes * w * h} elements");
                }
            }
        }
        
        Debug.Log("=== End of shape analysis ===");
    }

    // Add method to test input dimensions with a specific output name
    public bool TestInputDimensions(int width, int height, int channels, string testOutputName)
    {
        Debug.Log($"Testing dimensions: {width}x{height}x{channels} with output '{testOutputName}'");
        
        // Create a test tensor with specified dimensions
        Tensor testInput = null;
        IWorker testWorker = null;
        
        try
        {
            // Create tensor based on format
            if (isModelNHWCFormat)
            {
                testInput = new Tensor(1, height, width, channels);
            }
            else
            {
                testInput = new Tensor(1, channels, height, width);
            }
            
            // Create a temporary worker for testing
            Model testModel = ModelLoader.Load(ModelAsset);
            testWorker = WorkerFactory.CreateWorker(_backend, testModel);
            
            // Execute the test tensor
            testWorker.Execute(testInput);
            
            // Try to peek at the output tensor
            Tensor output = testWorker.PeekOutput(testOutputName) as Tensor;
            if (output == null)
            {
                Debug.LogError("Output tensor is null");
                return false;
            }
            
            // Success - the dimensions work
            Debug.Log($"Dimensions {width}x{height}x{channels} work with output '{testOutputName}'");
            Debug.Log($"Output tensor shape: {string.Join(" × ", GetSafeDimensions(output.shape))}");
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error testing dimensions: {e.Message}");
            return false;
        }
        finally
        {
            // Clean up resources
            if (testInput != null)
            {
                testInput.Dispose();
            }
            if (testWorker != null)
            {
                testWorker.Dispose();
            }
        }
    }

    /// <summary>
    /// Analyzes possible tensor shapes for a given size and class count
    /// </summary>
    /// <param name="tensorSize">The size (number of elements) of the tensor</param>
    /// <param name="classCount">The number of classes expected in the segmentation</param>
    private void AnalyzeTensorShapePossibilities(long tensorSize, int classCount)
    {
        if (tensorSize <= 0)
        {
            Debug.LogError("Invalid tensor size");
            return;
        }
        
        // Create shape array properly
        int[] possibleShape = new[] { 1, 0, 0, classCount };
        
        Debug.LogError($"Analyzing possible tensor shapes for size {tensorSize} with {classCount} classes...");
        
        // Find factors of tensorSize/classCount to determine possible height/width pairs
        long pixelCount = tensorSize / classCount;
        if (tensorSize % classCount != 0)
        {
            Debug.LogError($"Tensor size {tensorSize} is not divisible by class count {classCount}. This suggests a format mismatch.");
            return;
        }
        
        Debug.LogError($"Tensor has space for {pixelCount} pixels (tensorSize/classCount)");
        
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
            Debug.LogError("Possible dimensions (width x height):");
            foreach (var dim in possibleDimensions)
            {
                float ratio = (float)dim.x / dim.y;
                Debug.LogError($"- {dim.x} x {dim.y} (ratio: {ratio:F2})");
                
                // Suggest possible tensor shapes
                Debug.LogError($"  NHWC format: [1, {dim.y}, {dim.x}, {classCount}]");
                Debug.LogError($"  NCHW format: [1, {classCount}, {dim.y}, {dim.x}]");
            }
            
            // Try to find dimensions close to inputWidth/inputHeight
            Vector2Int closestDim = possibleDimensions[0];
            float targetRatio = (float)inputWidth / inputHeight;
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
            
            Debug.LogError($"Dimensions closest to input aspect ratio ({targetRatio:F2}): {closestDim.x} x {closestDim.y}");
        }
        else
        {
            Debug.LogError("No possible dimensions found that divide evenly.");
        }
    }

    /// <summary>
    /// Process a camera frame for segmentation
    /// </summary>
    /// <param name="texture">Input camera texture</param>
    /// <param name="targetResolution">Optional target resolution for processing</param>
    /// <returns>True if processing succeeded</returns>
    public bool ProcessCameraFrame(Texture2D texture, Vector2Int targetResolution)
    {
        try
        {
            // Resize texture to match expected input dimensions if needed
            Texture2D resizedTexture = ResizeTextureIfNeeded(texture, targetResolution);
            
            // Create input tensor from the texture
            Tensor inputTensor = null;
            try
            {
                inputTensor = CreateInputTensorFromTexture(resizedTexture);
                if (inputTensor == null)
                {
                    Debug.LogError("Failed to create input tensor from texture");
                    return false;
                }
                
                // Validate tensor dimensions before reshaping
                if (!ValidateTensorDimensions(inputTensor, inputWidth, inputHeight))
                {
                    return false;
                }
                
                // Execute the model
                ExecuteModel(inputTensor);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing camera frame: {e.Message}");
                if (e.Message.Contains("Cannot reshape"))
                {
                    HandleTensorReshapeError(inputTensor, e);
                }
                return false;
            }
            finally
            {
                // Always dispose of tensors to prevent memory leaks
                if (inputTensor != null)
                {
                    inputTensor.Dispose();
                    inputTensor = null;
                }
                
                // Clean up the resized texture if it's different from the input
                if (resizedTexture != null && resizedTexture != texture)
                {
                    Destroy(resizedTexture);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ProcessCameraFrame: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Validates tensor dimensions to ensure they match expected values
    /// </summary>
    private bool ValidateTensorDimensions(Tensor tensor, int expectedWidth, int expectedHeight)
    {
        if (tensor == null) return false;
        
        int tensorSize = tensor.shape.length;
        Debug.Log($"Tensor size: {tensorSize}, Expected size based on dimensions: {expectedWidth * expectedHeight * 3}");
        
        if (tensorSize != expectedWidth * expectedHeight * 3)
        {
            Debug.LogWarning($"Tensor dimensions mismatch. Expected {expectedWidth}x{expectedHeight}x3 but got size {tensorSize}");
            return false;
        }
        
        return true;
    }

    /// <summary>
    /// Handle tensor reshape errors with detailed diagnostics
    /// </summary>
    private void HandleTensorReshapeError(Tensor tensor, Exception error)
    {
        if (tensor == null)
        {
            Debug.LogError("Cannot diagnose reshape error: Tensor is null");
            return;
        }
        
        int tensorSize = tensor.length;
        Debug.LogError($"Tensor reshape error! Tensor size: {tensorSize}");
        Debug.LogError($"Expected dimensions: [n:1, h:{inputHeight}, w:{inputWidth}, c:3]");
        Debug.LogError($"Expected total size: {1 * inputHeight * inputWidth * 3}");
        Debug.LogError($"Actual size: {tensorSize}");
        Debug.LogError("Suggestion: Check that input texture dimensions match the model's expected input dimensions");
    }

    /// <summary>
    /// Resize texture to match the required input dimensions
    /// </summary>
    private Texture2D ResizeTextureIfNeeded(Texture2D texture, Vector2Int targetResolution)
    {
        // If target resolution is zero, use the default input dimensions
        if (targetResolution == Vector2Int.zero)
        {
            targetResolution = new Vector2Int(inputWidth, inputHeight);
        }
        
        // Check if we need to resize
        if (texture.width != targetResolution.x || texture.height != targetResolution.y)
        {
            Texture2D resized = new Texture2D(targetResolution.x, targetResolution.y, TextureFormat.RGBA32, false);
            Graphics.ConvertTexture(texture, resized);
            return resized;
        }
        
        return texture;
    }

    /// <summary>
    /// Create input tensor from texture
    /// </summary>
    private Tensor CreateInputTensorFromTexture(Texture2D texture)
    {
        if (texture == null) return null;
        
        try
        {
            // Create tensor with expected dimensions
            Tensor tensor;
            
            if (isModelNHWCFormat)
            {
                // NHWC format: [batch, height, width, channels]
                tensor = new Tensor(1, inputHeight, inputWidth, inputChannels);
            }
            else
            {
                // NCHW format: [batch, channels, height, width]
                tensor = new Tensor(1, inputChannels, inputHeight, inputWidth);
            }
            
            // Get pixel data from texture (assumes RGBA texture)
            Color32[] pixels = texture.GetPixels32();
            
            // Copy pixels to tensor
            for (int y = 0; y < inputHeight; y++)
            {
                for (int x = 0; x < inputWidth; x++)
                {
                    int pixelIndex = y * inputWidth + x;
                    
                    if (pixelIndex < pixels.Length)
                    {
                        Color32 pixel = pixels[pixelIndex];
                        
                        if (isModelNHWCFormat)
                        {
                            // NHWC format
                            tensor[0, y, x, 0] = pixel.r / 255.0f;
                            if (inputChannels > 1) tensor[0, y, x, 1] = pixel.g / 255.0f;
                            if (inputChannels > 2) tensor[0, y, x, 2] = pixel.b / 255.0f;
                        }
                        else
                        {
                            // NCHW format
                            tensor[0, 0, y, x] = pixel.r / 255.0f;
                            if (inputChannels > 1) tensor[0, 1, y, x] = pixel.g / 255.0f;
                            if (inputChannels > 2) tensor[0, 2, y, x] = pixel.b / 255.0f;
                        }
                    }
                }
            }
            
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating tensor from texture: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Execute the ML model with the given input tensor
    /// </summary>
    private void ExecuteModel(Tensor inputTensor)
    {
        if (_worker == null || inputTensor == null) return;
        
        Tensor outputTensor = null;
        
        try
        {
            // Execute the model
            _worker.Execute(inputTensor);
            
            // Get output
            outputTensor = _worker.PeekOutput(GetOutputTensorName()) as Tensor;
            
            if (outputTensor != null)
            {
                try
                {
                    // Process output tensor
                    ProcessOutputTensor(outputTensor);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing output tensor: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error executing model: {e.Message}");
        }
        finally
        {
            // Always dispose of the output tensor
            if (outputTensor != null)
            {
                outputTensor.Dispose();
            }
        }
    }

    /// <summary>
    /// Clean up resources when the component is destroyed
    /// </summary>
    private void DisposeWorker()
    {
        if (_worker != null)
        {
            _worker.Dispose();
            _worker = null;
        }
    }
    
    private string FormatIntArray(int[] array)
    {
        if (array == null || array.Length == 0)
        {
            return "[]";
        }
        return "[" + string.Join(", ", array) + "]";
    }

    /// <summary>
    /// Scales a texture to the target dimensions
    /// </summary>
    private Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        if (source == null) return null;
        
        // Create a temporary render texture
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
        
        // Copy source to render texture
        Graphics.Blit(source, rt);
        
        // Store active render texture
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        // Create new texture with target dimensions
        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        
        // Read pixels from render texture
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        
        // Restore previous active render texture
        RenderTexture.active = prevRT;
        
        // Release temporary render texture
        RenderTexture.ReleaseTemporary(rt);
        
        return result;
    }

    /// <summary>
    /// Process segmentation output and generate texture
    /// </summary>
    private void ProcessSegmentationOutput(Tensor outputTensor)
    {
        if (outputTensor == null) return;
        
        try
        {
            // Get dimensions safely
            int[] shapeDims = GetSafeDimensions(outputTensor.shape);
            
            if (shapeDims.Length < 3)
            {
                Debug.LogError("Invalid tensor shape for segmentation output");
                return;
            }
            
            // Extract dimensions based on format
            int height, width, channelCount;
            
            if (isModelNHWCFormat)
            {
                // NHWC: [N, H, W, C]
                height = shapeDims[1];
                width = shapeDims[2];
                channelCount = shapeDims.Length > 3 ? shapeDims[3] : 1;
            }
            else
            {
                // NCHW: [N, C, H, W]
                channelCount = shapeDims[1];
                height = shapeDims[2];
                width = shapeDims[3];
            }
            
            // Create or resize segmentation texture if needed
            if (_segmentationTexture == null || 
                _segmentationTexture.width != width || 
                _segmentationTexture.height != height)
            {
                if (_segmentationTexture != null)
                {
                    Destroy(_segmentationTexture);
                }
                
                #if UNITY_2022_1_OR_NEWER
                // For newer Unity versions (2022.1+)
                _segmentationTexture = new Texture2D(
                    width, 
                    height, 
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                #else
                // For older Unity versions
                _segmentationTexture = new Texture2D(
                    width, 
                    height, 
                    TextureFormat.R8, 
                    false);
                #endif
            }
            
            // Create pixel data array
            Color32[] pixelData = new Color32[width * height];
            
            // Process each pixel
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // Get the classification value for wall class using safe method
                    float classValue = GetValueFromTensor(outputTensor, 0, y, x, wallClassId);
                    
                    // Apply threshold for classification (0 or 255)
                    byte intensity = classValue >= classificationThreshold ? (byte)255 : (byte)0;
                    
                    // Set pixel data
                    pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
                }
            }
            
            // Apply pixel data to texture
            _segmentationTexture.SetPixels32(pixelData);
            _segmentationTexture.Apply();
            
            // Invoke the completion callback
            onProcessingComplete?.Invoke(_segmentationTexture);
            OnSegmentationCompleted?.Invoke(_segmentationTexture);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing segmentation output: {e.Message}");
        }
    }

    /// <summary>
    /// Get value from tensor at specified coordinates
    /// </summary>
    private float GetValueFromTensor(Tensor tensor, int batch, int y, int x, int channel)
    {
        if (tensor == null) return 0f;
        
        try
        {
            int[] dims = GetSafeDimensions(tensor.shape);
            
            if (isModelNHWCFormat)
            {
                // NHWC format: [batch, height, width, channels]
                return tensor[batch, y, x, channel];
            }
            else
            {
                // NCHW format: [batch, channels, height, width]
                return tensor[batch, channel, y, x];
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting value from tensor: {e.Message}");
            return 0f;
        }
    }

    /// <summary>
    /// Process output tensor from the model
    /// </summary>
    private void ProcessOutputTensor(Tensor outputTensor)
    {
        ProcessSegmentationOutput(outputTensor);
    }
} 