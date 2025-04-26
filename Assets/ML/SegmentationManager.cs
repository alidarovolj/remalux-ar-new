using UnityEngine;
using Unity.Barracuda;
using System.Threading;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;

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
    [SerializeField] private NNModel ModelAsset;
    [SerializeField] private string inputName = "images";
    [SerializeField] private string outputName = "output_segmentations";
    [SerializeField] private bool isModelNHWCFormat = true;
    [SerializeField] private int inputWidth = 513;
    [SerializeField] private int inputHeight = 513;
    [SerializeField] private int inputChannels = 3;
    [SerializeField] private int segmentationClassCount = 2; // Number of output classes (background + wall)
    [Range(0, 255)]
    [SerializeField] private int wallClassId = 1;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float classificationThreshold = 0.5f;
    [SerializeField] private int processingInterval = 2;
    [SerializeField] private bool debugMode = false;

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

    private void InitializeModel()
    {
        if (ModelAsset == null)
        {
            Debug.LogError("Model asset is not assigned");
            return;
        }

        try
        {
            // Загружаем модель
            _runtimeModel = ModelLoader.Load(ModelAsset);
            
            if (_runtimeModel == null)
            {
                Debug.LogError("Failed to load the model");
                return;
            }

            // Создаем worker для выполнения модели
            _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, _runtimeModel);
            
            if (debugMode)
            {
                Debug.Log($"Model loaded successfully: {ModelAsset.name}");
                LogTensorDimensions();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error initializing the model: {e.Message}\n{e.StackTrace}");
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
            
            {
                if (debugMode)
                {
                    Debug.Log($"Output tensor shape: dimensions={output.shape}");
                }
                
                // Check if this is a valid shape for segmentation (should have at least 3 dimensions)
                if (output.shape.rank >= 3)
                {
                    return output.shape;
                }
                else
                {
                    // If shape is too simple, try to infer from input dimensions
                    Debug.LogWarning($"Output tensor has insufficient dimensions, attempting to reshape");
                    
                    // Check inputShape dimension count directly
                    if (inputShape.rank >= 4)
                    {
                        // Determine if height and width are at index 1,2 (NCHW) or 1,2 (NHWC)
                        int height = inputShape[1];
                        int width = inputShape[2];
                        
                        // For DeepLab models, output is often [1, numClasses, height, width] or [1, height, width, numClasses]
                        if (output.shape[0] == 1)
                        {
                            if (output.length == height * width * segmentationClassCount)
                            {
                                // Shape is likely [1, H, W, C] or [1, C, H, W] but flattened
                                return new TensorShape(1, height, width, segmentationClassCount);
                            }
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
            Debug.LogError($"Error in ProcessCameraFrame: {e.Message}\n{e.StackTrace}");
            _isProcessing = false;
            return false;
        }
    }
    
    /// <summary>
    /// Resize source texture to target dimensions
    /// </summary>
    private void ResizeTexture(Texture2D source, Texture2D destination)
    {
        // Проверка на null-ссылки
        if (source == null || destination == null)
        {
            Debug.LogError($"ResizeTexture: Source or destination texture is null. Source: {source != null}, Destination: {destination != null}");
            return;
        }
        
        // Проверка размеров текстур
        if (destination.width <= 0 || destination.height <= 0)
        {
            Debug.LogError($"ResizeTexture: Invalid destination dimensions: {destination.width}x{destination.height}");
            return;
        }
        
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
    private bool ConvertTensorToTexture(Tensor output, TensorShape shape)
    {
        if (_segmentationTexture == null || 
            _segmentationTexture.width != shape[2] || 
            _segmentationTexture.height != shape[1])
        {
            if (_segmentationTexture != null)
            {
                Destroy(_segmentationTexture);
            }
            
            _segmentationTexture = new Texture2D(
                shape[2], 
                shape[1], 
                TextureFormat.R8, false);
        }
        
        try
        {
            // Подготовить данные для текстуры
            Color32[] pixelData = new Color32[shape[2] * shape[1]];
            
            // Обработать данные выходного тензора
            for (int y = 0; y < shape[1]; y++)
            {
                for (int x = 0; x < shape[2]; x++)
                {
                    // Индекс пикселя в текстуре
                    int pixelIndex = y * shape[2] + x;
                    
                    // Получить значение сегментации для текущего пикселя
                    float value = 0;
                    
                    // Определить значение сегментации в зависимости от формата модели
                    if (isModelNHWCFormat)
                    {
                        // NHWC: [batch, height, width, channels]
                        // Для классификации стен берем значение соответствующего класса
                        if (wallClassId < shape[3])
                        {
                            // [0, y, x, wallClassId]
                            value = output[0, y, x, wallClassId];
                        }
                    }
                    else
                    {
                        // NCHW: [batch, channels, height, width]
                        // Для классификации стен берем значение соответствующего класса
                        if (wallClassId < shape[1])
                        {
                            // [0, wallClassId, y, x]
                            value = output[0, wallClassId, y, x];
                        }
                    }
                    
                    // Применить порог для выделения стен
                    byte intensity = value >= classificationThreshold ? (byte)255 : (byte)0;
                    
                    // Записать значение в текстурные данные
                    pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
                }
            }
            
            // Применить данные к текстуре и обновить ее
            _segmentationTexture.SetPixels32(pixelData);
            _segmentationTexture.Apply();
            
            // Уведомить о завершении обработки через оба события
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
        if (_runtimeModel == null)
        {
            Debug.LogError("Cannot log tensor dimensions - model is not loaded");
            return;
        }
        
        Debug.Log("==== MODEL TENSOR DIMENSIONS ====");
        Debug.Log("INPUTS:");
        foreach (var input in _runtimeModel.inputs)
        {
            // Changed to just output input name without trying to access shape property
            Debug.Log($"- Input: {input}");
        }
        
        Debug.Log("OUTPUTS:");
        foreach (var output in _runtimeModel.outputs)
        {
            // Changed to just output output name without trying to access shape property
            Debug.Log($"- Output: {output}");
        }
        
        Debug.Log("=================================");
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
} 