using UnityEngine;
using Unity.Barracuda;
using System.Threading;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Experimental.Rendering;  // Add this for TextureCreationFlags

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

    private void OnDestroy()
    {
        _worker?.Dispose();
    }

    private void InitializeTextures()
    {
        // Инициализируем входную текстуру
        _inputTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
        
        // Инициализируем выходную текстуру
        _segmentationTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.R8, false);
        
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
                Debug.Log($"Output tensor shape: dimensions={string.Join(",", output.shape.dimensions)}");
            }
            
            // Check if this is a valid shape for segmentation (should have at least 3 dimensions)
            if (output.shape.dimensions.Length >= 3)
            {
                return output.shape;
            }
            else
            {
                // If shape is too simple, try to infer from input dimensions
                Debug.LogWarning($"Output tensor has insufficient dimensions, attempting to reshape");
                
                // Check inputShape dimension count directly
                if (inputShape.dimensions.Length >= 4)
                {
                    // Determine if height and width are at index 1,2 (NCHW) or 1,2 (NHWC)
                    int height = inputShape.dimensions[1];
                    int width = inputShape.dimensions[2];
                    
                    // For DeepLab models, output is often [1, numClasses, height, width] or [1, height, width, numClasses]
                    if (output.shape.dimensions[0] == 1)
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
    /// Обрабатывает текстуру камеры, выполняя сегментацию с помощью модели машинного обучения
    /// (Texture2D overload for compatibility with existing code)
    /// </summary>
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
            if (outputShape.dimensions[0] == 0)
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
    
    /// <summary>
    /// Resize source texture to target dimensions
    /// </summary>
    /// <returns>Resized texture</returns>
    private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        // Проверка на null-ссылки
        if (source == null)
        {
            Debug.LogError("ResizeTexture: Source texture is null");
            return null;
        }
        
        // Проверка размеров текстур
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            Debug.LogError($"ResizeTexture: Invalid destination dimensions: {targetWidth}x{targetHeight}");
            return null;
        }

        // Create a new texture with target dimensions
        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        
        // Use bilinear scaling via RenderTexture for efficiency
        RenderTexture rt = RenderTexture.GetTemporary(
            targetWidth, 
            targetHeight, 
            0, 
            RenderTextureFormat.ARGB32
        );
        
        Graphics.Blit(source, rt);
        
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply();
        
        RenderTexture.active = prevRT;
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
            // Check if we need to create or resize the output texture
            if (_segmentationTexture == null || 
                _segmentationTexture.width != shape.dimensions[2] || 
                _segmentationTexture.height != shape.dimensions[1])
            {
                if (_segmentationTexture != null)
                {
                    Destroy(_segmentationTexture);
                }
                
                #if UNITY_2019_1_OR_NEWER
                // For newer Unity versions (2019.1+)
                _segmentationTexture = new Texture2D(
                    shape.dimensions[2], 
                    shape.dimensions[1], 
                    TextureFormat.R8, 
                    false);
                #else
                // For older Unity versions
                _segmentationTexture = new Texture2D(
                    shape.dimensions[2], 
                    shape.dimensions[1], 
                    TextureFormat.R8, 
                    false,
                    false);
                #endif
            }
            
            // Prepare data for texture
            Color32[] pixelData = new Color32[shape.dimensions[2] * shape.dimensions[1]];
            
            // Process output tensor data
            for (int y = 0; y < shape.dimensions[1]; y++)
            {
                for (int x = 0; x < shape.dimensions[2]; x++)
                {
                    // Pixel index in texture
                    int pixelIndex = y * shape.dimensions[2] + x;
                    
                    // Get segmentation value for current pixel
                    float value = 0;
                    
                    // Determine segmentation value based on model format
                    if (isModelNHWCFormat)
                    {
                        // NHWC: [batch, height, width, channels]
                        // For wall classification, get the value of the corresponding class
                        if (wallClassId < shape.dimensions[3])
                        {
                            // [0, y, x, wallClassId]
                            value = output[0, y, x, wallClassId];
                        }
                    }
                    else
                    {
                        // NCHW: [batch, channels, height, width]
                        // For wall classification, get the value of the corresponding class
                        if (wallClassId < shape.dimensions[1])
                        {
                            // [0, wallClassId, y, x]
                            value = output[0, wallClassId, y, x];
                        }
                    }
                    
                    // Apply threshold for wall detection
                    byte intensity = value >= classificationThreshold ? (byte)255 : (byte)0;
                    
                    // Set texture data
                    pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
                }
            }
            
            // Apply data to texture and update it
            _segmentationTexture.SetPixels32(pixelData);
            _segmentationTexture.Apply();
            
            // Notify about processing completion through both events
            onProcessingComplete?.Invoke(_segmentationTexture);
            OnSegmentationCompleted?.Invoke(_segmentationTexture);
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in ConvertTensorToTexture: {e.Message}\n{e.StackTrace}");
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
        
        // Log configuration
        Debug.Log($"Input dimensions: {inputWidth}x{inputHeight}x{inputChannels}, NHWC format: {isModelNHWCFormat}");
        Debug.Log($"Output: name='{outputName}', class count={segmentationClassCount}, wall class={wallClassId}");
    }

    // Add new method to log model input shape
    public void LogModelInputShape()
    {
        if (!IsModelInitialized())
        {
            Debug.LogError("Cannot log model input shape - model is not initialized");
            return;
        }
        
        Debug.Log("=== MODEL INPUT SHAPE ===");
        
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
        
        // Execute model with dummy input
        try
        {
            _worker.Execute(dummyInput);
            
            // Log the input dimensions
            Debug.Log($"Input tensor: {inputWidth}x{inputHeight}x{inputChannels}");
            Debug.Log($"Input tensor format: {(isModelNHWCFormat ? "NHWC" : "NCHW")}");
            
            int[] dims = dummyInput.shape.dimensions;
            Debug.Log($"Input tensor shape: {string.Join("×", dims)}");
            Debug.Log($"Input tensor length: {dummyInput.length}");
            
            dummyInput.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error executing model with dummy input: {e.Message}");
            dummyInput.Dispose();
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
        Tensor dummyInput;
        
        if (isModelNHWCFormat)
        {
            dummyInput = new Tensor(1, inputHeight, inputWidth, inputChannels);
        }
        else
        {
            dummyInput = new Tensor(1, inputChannels, inputHeight, inputWidth);
        }
        
        // Execute model with dummy input to populate outputs
        try
        {
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
                        int[] outputDims = outputTensor.shape.dimensions;
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
            
            dummyInput.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error executing model with dummy input: {e.Message}");
            dummyInput.Dispose();
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
        // Modified to check only for AndroidYuv420_888 format since IosYpCbCr420_8BiPlanar is not available
        if (image.format != XRCpuImage.Format.AndroidYuv420_888)
        {
            Debug.LogError($"Unsupported image format: {image.format}");
            return null;
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
                
                _inputTexture = new Texture2D(targetDims.x, targetDims.y, TextureFormat.RGBA32, false);
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
                    Debug.Log($"Model input shape: {modelInputShape}");
                }
                
                // Check if we have NCHW or NHWC format by looking at the last dimension
                // Most models use NHWC (batch, height, width, channels) format where the 
                // 4th dimension (index 3) is the channel count
                if (modelInputShape.Length >= 4)
                {
                    // Different models have different input formats (NCHW vs NHWC)
                    // We'll need to adjust accordingly - try to detect which format
                    channels = modelInputShape[3] == 3 ? 3 : modelInputShape[1];
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
            Tensor inputTensor = PrepareInputTensorFromXRCpuImage(image);
            
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
            Tensor outputTensor = _worker.PeekOutput(outputName);

            if (outputTensor == null)
            {
                Debug.LogError($"Failed to retrieve output tensor '{outputName}'");
                return;
            }

            try
            {
                // Create a valid input shape for DetermineOutputShape
                var inputShape = new TensorShape(1, inputHeight, inputWidth, inputChannels);
                TensorShape outputShape = DetermineOutputShape(outputTensor, inputShape);
                
                // Convert the output tensor to a texture
                ConvertTensorToTexture(outputTensor, outputShape);
            }
            catch (Exception e)
            {
                HandleTensorReshapeError(outputTensor, e, $"[{inputHeight}, {inputWidth}, {inputChannels}]");
            }
            
            // Clean up
            inputTensor.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing camera frame: {e.Message}\n{e.StackTrace}");
        }
    }

    // Helper method to prepare input tensor from XRCpuImage
    private Tensor PrepareInputTensorFromXRCpuImage(XRCpuImage image)
    {
        Vector2Int targetDims = new Vector2Int(inputWidth, inputHeight);
        
        // Modified to check only for AndroidYuv420_888 format since IosYpCbCr420_8BiPlanar is not available
        if (image.format != XRCpuImage.Format.AndroidYuv420_888)
        {
            Debug.LogError($"Unsupported image format: {image.format}");
            return null;
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
                
                _inputTexture = new Texture2D(targetDims.x, targetDims.y, TextureFormat.RGBA32, false);
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
        this.inputWidth = width;
        this.inputHeight = height;
        this.inputChannels = channels;
        
        Debug.Log($"Set input dimensions to: {width}x{height}x{channels}");
        
        // Reinitialize textures with new dimensions
        if (_inputTexture != null)
        {
            Destroy(_inputTexture);
            _inputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }
        
        if (_segmentationTexture != null)
        {
            Destroy(_segmentationTexture);
            _segmentationTexture = new Texture2D(width, height, TextureFormat.R8, false);
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
            Tensor outputTensor = _worker.PeekOutput(outputName);
            if (outputTensor != null)
            {
                // Get the dimensions array explicitly
                int[] outputDims = outputTensor.shape.dimensions;
                
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

    private bool ValidateInputDimensions(Tensor inputTensor)
    {
        if (inputTensor == null)
        {
            Debug.LogError("Cannot validate dimensions of null input tensor");
            return false;
        }
        
        // Check number of dimensions
        if (inputTensor.shape.dimensions.Length != 4)
        {
            Debug.LogError($"Input tensor has wrong number of dimensions: expected 4, got {inputTensor.shape.dimensions.Length}");
            return false;
        }
        
        // Check dimensions based on format
        if (isModelNHWCFormat)
        {
            // NHWC format: [batch, height, width, channels]
            if (inputTensor.shape.dimensions[1] != inputHeight || 
                inputTensor.shape.dimensions[2] != inputWidth || 
                inputTensor.shape.dimensions[3] != inputChannels)
            {
                Debug.LogWarning($"Input tensor dimensions don't match configuration. " +
                               $"Expected: [1, {inputHeight}, {inputWidth}, {inputChannels}], " +
                               $"Got: [1, {inputTensor.shape.dimensions[1]}, {inputTensor.shape.dimensions[2]}, {inputTensor.shape.dimensions[3]}]");
                return false;
            }
        }
        else
        {
            // NCHW format: [batch, channels, height, width]
            if (inputTensor.shape.dimensions[1] != inputChannels || 
                inputTensor.shape.dimensions[2] != inputHeight || 
                inputTensor.shape.dimensions[3] != inputWidth)
            {
                Debug.LogWarning($"Input tensor dimensions don't match configuration. " +
                               $"Expected: [1, {inputChannels}, {inputHeight}, {inputWidth}], " +
                               $"Got: [1, {inputTensor.shape.dimensions[1]}, {inputTensor.shape.dimensions[2]}, {inputTensor.shape.dimensions[3]}]");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Handles tensor reshape errors by providing detailed diagnostics and suggestions
    /// </summary>
    /// <param name="outputTensor">The tensor that failed to reshape</param>
    /// <param name="ex">The exception that was thrown</param>
    /// <param name="expectedShape">The expected shape for the tensor</param>
    private void HandleTensorReshapeError(Tensor outputTensor, Exception ex, int[] expectedShape)
    {
        Debug.LogError($"Error during tensor reshaping: {ex.Message}");
        Debug.LogError($"Output tensor shape: [{string.Join(", ", outputTensor.shape)}], Expected: [{string.Join(", ", expectedShape)}]");
        Debug.LogError($"Output tensor element count: {outputTensor.length}, ClassCount: {segmentationClassCount}");
        
        // Find ModelConfigFixer to analyze tensor shape possibilities
        ModelConfigFixer configFixer = FindObjectOfType<ModelConfigFixer>();
        if (configFixer != null)
        {
            Debug.Log("Using ModelConfigFixer to analyze tensor shape possibilities...");
            configFixer.AnalyzeTensorShapePossibilities(outputTensor.length, segmentationClassCount);
        }
        else
        {
            Debug.LogWarning("ModelConfigFixer not found in scene. Cannot analyze tensor shape possibilities.");
        }
        
        Debug.LogError("Troubleshooting suggestions:");
        Debug.LogError("1. Verify your model's output format (NCHW or NHWC)");
        Debug.LogError("2. Use ModelConfigFixer.ExamineModel() to analyze the model");
        Debug.LogError("3. Ensure the model's output dimensions match the configured dimensions");
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
            Debug.LogError("PrepareInputTensorFromTexture: Input texture is null");
            return null;
        }

        try
        {
            Texture2D resizedTexture = null;
            
            // Resize texture to match model input dimensions if needed
            if (texture.width != inputWidth || texture.height != inputHeight)
            {
                if (debugMode)
                {
                    Debug.Log($"Resizing texture from {texture.width}x{texture.height} to {inputWidth}x{inputHeight}");
                }
                
                resizedTexture = ResizeTexture(texture, inputWidth, inputHeight);
            }
            else
            {
                // Use the original texture
                resizedTexture = texture;
            }
            
            if (resizedTexture == null)
            {
                Debug.LogError("PrepareInputTensorFromTexture: Failed to resize texture");
                return null;
            }

            // Create input tensor with expected dimensions
            Tensor inputTensor = null;
            
            if (isModelNHWCFormat)
            {
                // NHWC format: 1 x H x W x 3
                inputTensor = new Tensor(1, inputHeight, inputWidth, 3);
                
                // Copy texture data to tensor
                for (int y = 0; y < inputHeight; y++)
                {
                    for (int x = 0; x < inputWidth; x++)
                    {
                        Color pixel = resizedTexture.GetPixel(x, y);
                        inputTensor[0, y, x, 0] = pixel.r;
                        inputTensor[0, y, x, 1] = pixel.g;
                        inputTensor[0, y, x, 2] = pixel.b;
                    }
                }
            }
            else
            {
                // NCHW format: 1 x 3 x H x W
                inputTensor = new Tensor(1, 3, inputHeight, inputWidth);
                
                // Copy texture data to tensor
                for (int y = 0; y < inputHeight; y++)
                {
                    for (int x = 0; x < inputWidth; x++)
                    {
                        Color pixel = resizedTexture.GetPixel(x, y);
                        inputTensor[0, 0, y, x] = pixel.r;
                        inputTensor[0, 1, y, x] = pixel.g;
                        inputTensor[0, 2, y, x] = pixel.b;
                    }
                }
            }
            
            // Clean up the resized texture if it's not the original
            if (resizedTexture != texture)
            {
                Destroy(resizedTexture);
            }
            
            return inputTensor;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error preparing input tensor from texture: {e.Message}\n{e.StackTrace}");
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
            Tensor output = _worker.PeekOutput(_runtimeModel.outputs[0]);
            
            if (output == null)
            {
                Debug.LogError("Failed to get output tensor from model");
                input.Dispose();
                _isProcessing = false;
                return false;
            }
            
            // Determine the output tensor shape from the model
            TensorShape outputShape = DetermineOutputShape(output, input.shape);
            if (outputShape[0] == 0)
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
        if (!IsModelInitialized())
        {
            Debug.LogError("Cannot test dimensions - model is not initialized");
            return false;
        }
        
        Debug.Log($"Testing dimensions: {width}x{height}x{channels} with output '{testOutputName}'");
        
        // Create a test tensor with specified dimensions
        Tensor testInput = null;
        
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
            
            // Fill with zeros (or could use random values)
            for (int i = 0; i < testInput.length; i++)
            {
                testInput[i] = 0f;
            }
            
            // Try to execute the model
            _worker.Execute(testInput);
            
            // Check if the output tensor exists
            bool outputExists = false;
            foreach (var output in _runtimeModel.outputs)
            {
                if (output == testOutputName)
                {
                    outputExists = true;
                    break;
                }
            }
            
            if (!outputExists)
            {
                Debug.LogWarning($"Output '{testOutputName}' not found in model outputs");
                testInput.Dispose();
                return false;
            }
            
            // Try to peek at the output
            Tensor outputTensor = _worker.PeekOutput(testOutputName);
            if (outputTensor != null)
            {
                // Get dimensions as array
                int[] outputDims = outputTensor.shape.dimensions;
                
                Debug.Log($"Success! Output '{testOutputName}' shape: {string.Join(" × ", outputDims)}");
                Debug.Log($"Output length: {outputTensor.length} elements");
                
                testInput.Dispose();
                return true;
            }
            else
            {
                Debug.LogError("Output tensor is null");
                testInput.Dispose();
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error testing dimensions: {e.Message}");
            if (testInput != null) testInput.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Analyzes possible tensor shapes for a given size and class count
    /// </summary>
    /// <param name="tensorSize">The size (number of elements) of the tensor</param>
    /// <param name="classCount">The number of classes expected in the segmentation</param>
    private void AnalyzeTensorShapePossibilities(long tensorSize, int classCount)
    {
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
    /// Обрабатывает кадр камеры, выполняя сегментацию с помощью модели машинного обучения
    /// </summary>
    public bool ProcessCameraFrame(Texture2D cameraTexture)
    {
        try
        {
            if (!IsModelInitialized())
            {
                Debug.LogWarning("ML model not initialized. Cannot process the camera frame.");
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

            // Create a dummy target resolution if needed
            Vector2Int targetDims = new Vector2Int(inputWidth, inputHeight);
            
            // Prepare input tensor from camera texture
            using (var inputTensor = PrepareInputTensor(cameraTexture, targetDims))
            {
                if (inputTensor == null)
                {
                    Debug.LogError("Failed to prepare input tensor from camera texture");
                    _isProcessing = false;
                    return false;
                }

                // Execute neural network
                _worker.Execute(inputTensor);

                // Get output tensor
                var outputTensor = _worker.PeekOutput(outputName);
                
                if (outputTensor == null)
                {
                    Debug.LogError($"[SegmentationManager] Output tensor is null. Output layer name: {outputName}");
                    LogAvailableOutputs();
                    _isProcessing = false;
                    return false;
                }
                
                try
                {
                    // Determine expected output shape based on configuration
                    int[] expectedOutputShape;
                    if (isModelNHWCFormat)
                    {
                        expectedOutputShape = new int[] { 1, inputHeight, inputWidth, segmentationClassCount };
                    }
                    else // NCHW
                    {
                        expectedOutputShape = new int[] { 1, segmentationClassCount, inputHeight, inputWidth };
                    }
                    
                    // Get actual tensor shape for comparison
                    TensorShape outputShape = DetermineOutputShape(outputTensor, inputTensor.shape);
                    if (outputShape.dimensions[0] == 0)
                    {
                        Debug.LogWarning("Failed to determine valid output shape, trying to continue with actual tensor shape");
                        outputShape = outputTensor.shape;
                    }
                    
                    // Convert tensor to texture with wall class highlighted
                    bool success = ConvertTensorToTexture(outputTensor, outputShape);
                    
                    // Clean up resources
                    outputTensor.Dispose();
                    _isProcessing = false;
                    return success;
                }
                catch (Exception e)
                {
                    // Prepare expected shape for error handler
                    int[] expectedOutputShape;
                    if (isModelNHWCFormat)
                    {
                        expectedOutputShape = new int[] { 1, inputHeight, inputWidth, segmentationClassCount };
                    }
                    else // NCHW
                    {
                        expectedOutputShape = new int[] { 1, segmentationClassCount, inputHeight, inputWidth };
                    }
                    
                    HandleTensorReshapeError(outputTensor, e, expectedOutputShape);
                    outputTensor?.Dispose();
                    _isProcessing = false;
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SegmentationManager] Error processing camera frame: {e.Message}");
            Debug.LogException(e);
            _isProcessing = false;
            return false;
        }
    }

    private Texture2D ConvertOutputToTexture(Tensor outputTensor)
    {
        if (outputTensor == null)
        {
            Debug.LogError("Output tensor is null in ConvertOutputToTexture");
            return null;
        }

        TensorShape outputShape = DetermineOutputShape(outputTensor, new TensorShape(1, inputHeight, inputWidth, inputChannels));
        if (outputShape.dimensions[0] == 0)
        {
            Debug.LogError("Failed to determine valid output shape");
            return null;
        }

        // Create texture with correct dimensions
        #if UNITY_2019_1_OR_NEWER
        // For newer Unity versions (2019.1+)
        Texture2D segmentationTexture = new Texture2D(
            outputShape.dimensions[2], 
            outputShape.dimensions[1], 
            TextureFormat.R8, 
            false);
        #else
        // For older Unity versions
        Texture2D segmentationTexture = new Texture2D(
            outputShape.dimensions[2], 
            outputShape.dimensions[1], 
            TextureFormat.R8, 
            false,
            false);
        #endif
            
        // Prepare pixel data array
        Color32[] pixelData = new Color32[outputShape.dimensions[2] * outputShape.dimensions[1]];

        // Process tensor data into pixels
        for (int y = 0; y < outputShape.dimensions[1]; y++)
        {
            for (int x = 0; x < outputShape.dimensions[2]; x++)
            {
                int pixelIndex = y * outputShape.dimensions[2] + x;
                float value = 0;

                if (isModelNHWCFormat)
                {
                    value = outputTensor[0, y, x, wallClassId];
                }
                else
                {
                    value = outputTensor[0, wallClassId, y, x];
                }

                byte intensity = value >= classificationThreshold ? (byte)255 : (byte)0;
                pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
            }
        }

        // Apply pixel data to texture
        segmentationTexture.SetPixels32(pixelData);
        segmentationTexture.Apply();

        return segmentationTexture;
    }

    // Add an overload for Texture2D
    private Tensor PrepareInputTensor(Texture2D texture, Vector2Int targetDims, bool normalizeInput = true)
    {
        if (texture == null)
        {
            Debug.LogError("Cannot prepare input tensor from null texture");
            return null;
        }

        try
        {
            // Resize texture if needed
            Texture2D resizedTexture = null;
            if (texture.width != targetDims.x || texture.height != targetDims.y)
            {
                resizedTexture = ResizeTexture(texture, targetDims.x, targetDims.y);
            }
            else
            {
                resizedTexture = texture;
            }

            if (resizedTexture == null)
            {
                Debug.LogError("Failed to prepare input texture");
                return null;
            }

            // Create input tensor with expected dimensions
            Tensor inputTensor = null;
            
            if (isModelNHWCFormat)
            {
                // NHWC format: 1 x H x W x 3
                inputTensor = new Tensor(1, targetDims.y, targetDims.x, inputChannels);
                
                // Copy texture data to tensor
                for (int y = 0; y < targetDims.y; y++)
                {
                    for (int x = 0; x < targetDims.x; x++)
                    {
                        Color pixel = resizedTexture.GetPixel(x, y);
                        float r = normalizeInput ? pixel.r : pixel.r / 255.0f;
                        float g = normalizeInput ? pixel.g : pixel.g / 255.0f;
                        float b = normalizeInput ? pixel.b : pixel.b / 255.0f;
                        
                        inputTensor[0, y, x, 0] = r;
                        inputTensor[0, y, x, 1] = g;
                        inputTensor[0, y, x, 2] = b;
                    }
                }
            }
            else
            {
                // NCHW format: 1 x 3 x H x W
                inputTensor = new Tensor(1, inputChannels, targetDims.y, targetDims.x);
                
                // Copy texture data to tensor
                for (int y = 0; y < targetDims.y; y++)
                {
                    for (int x = 0; x < targetDims.x; x++)
                    {
                        Color pixel = resizedTexture.GetPixel(x, y);
                        float r = normalizeInput ? pixel.r : pixel.r / 255.0f;
                        float g = normalizeInput ? pixel.g : pixel.g / 255.0f;
                        float b = normalizeInput ? pixel.b : pixel.b / 255.0f;
                        
                        inputTensor[0, 0, y, x] = r;
                        inputTensor[0, 1, y, x] = g;
                        inputTensor[0, 2, y, x] = b;
                    }
                }
            }
            
            // Clean up if we created a new texture
            if (resizedTexture != texture)
            {
                Destroy(resizedTexture);
            }
            
            return inputTensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing input tensor from texture: {e.Message}");
            return null;
        }
    }
} 