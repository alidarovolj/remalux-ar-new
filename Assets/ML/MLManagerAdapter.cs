using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;
using System.Collections;
using UnityEngine.XR.ARSubsystems;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// Адаптер для совместимости между ARMLController и SegmentationManager
/// </summary>
[RequireComponent(typeof(ARCameraManager))]
public class MLManagerAdapter : MonoBehaviour
{
    [Header("Объекты для связывания")]
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private MaskProcessor maskProcessor;
    [SerializeField] private DeepLabPredictor deepLabPredictor;

    [Header("Настройки")]
    [SerializeField] private float predictionInterval = 0.5f;
    [SerializeField] private bool autoStartPrediction = true;
    [SerializeField] private bool debugMode = false;
    [SerializeField] private int downsampleFactor = 4;
    [SerializeField] private bool processOnEveryFrame = false;
    [SerializeField] private float processingInterval = 0.5f;
    [SerializeField] private int captureWidth = 576;
    [SerializeField] private int captureHeight = 256;
    
    private Texture2D _currentFrameTexture;
    private Texture2D _cameraTexture;
    private Coroutine _predictionCoroutine;
    private bool _isPredicting = false;
    private float lastProcessingTime = 0f;
    
    private ARCameraManager cameraManager;
    
    private void Awake()
    {
        cameraManager = GetComponent<ARCameraManager>();
        #if UNITY_2022_1_OR_NEWER
        _cameraTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
        #else
        _cameraTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
        #endif
        lastProcessingTime = 0f;
        FindMissingReferences();
    }
    
    private void OnEnable()
    {
        cameraManager.frameReceived += OnCameraFrameReceived;
        // Автозапуск предсказаний при включении
        if (autoStartPrediction)
            StartContinuousPrediction();
    }
    
    private void OnDisable()
    {
        cameraManager.frameReceived -= OnCameraFrameReceived;
        StopContinuousPrediction();
        
        // Clean up textures
        if (_cameraTexture != null)
        {
            Destroy(_cameraTexture);
            _cameraTexture = null;
        }
        
        if (_currentFrameTexture != null)
        {
            Destroy(_currentFrameTexture);
            _currentFrameTexture = null;
        }
    }
    
    private void Start()
    {
        if (debugMode)
            Debug.Log("MLManagerAdapter: Initialized and ready");
    }
    
    /// <summary>
    /// Поиск недостающих ссылок на компоненты
    /// </summary>
    private void FindMissingReferences()
    {
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager != null && debugMode)
                Debug.Log("MLManagerAdapter: Found ARCameraManager automatically");
        }
        
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager != null && debugMode)
                Debug.Log("MLManagerAdapter: Found SegmentationManager automatically");
        }
        
        if (maskProcessor == null)
        {
            maskProcessor = FindObjectOfType<MaskProcessor>();
            if (maskProcessor != null && debugMode)
                Debug.Log("MLManagerAdapter: Found MaskProcessor automatically");
        }
        
        if (deepLabPredictor == null)
        {
            deepLabPredictor = FindObjectOfType<DeepLabPredictor>();
            if (deepLabPredictor != null && debugMode)
                Debug.Log("MLManagerAdapter: Found DeepLabPredictor automatically");
            
            // Также проверяем EnhancedDeepLabPredictor
            if (deepLabPredictor == null)
            {
                var components = FindObjectsOfType<Component>();
                foreach (var component in components)
                {
                    if (component.GetType().Name.Contains("EnhancedDeepLabPredictor"))
                    {
                        deepLabPredictor = component as DeepLabPredictor;
                        if (deepLabPredictor != null && debugMode)
                            Debug.Log("MLManagerAdapter: Found EnhancedDeepLabPredictor automatically");
                        break;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// API-метод, который ARMLController ожидает для запуска предсказаний
    /// </summary>
    public void StartContinuousPrediction()
    {
        if (_isPredicting)
            return;
            
        if (segmentationManager == null || arCameraManager == null)
        {
            Debug.LogWarning("MLManagerAdapter: Cannot start prediction - missing SegmentationManager or ARCameraManager");
            FindMissingReferences();
            return;
        }
        
        _isPredicting = true;
        _predictionCoroutine = StartCoroutine(PredictionCoroutine());
        
        if (debugMode)
            Debug.Log("MLManagerAdapter: Started continuous prediction");
    }
    
    /// <summary>
    /// API-метод, который ARMLController ожидает для остановки предсказаний
    /// </summary>
    public void StopContinuousPrediction()
    {
        if (!_isPredicting)
            return;
            
        _isPredicting = false;
        
        if (_predictionCoroutine != null)
        {
            StopCoroutine(_predictionCoroutine);
            _predictionCoroutine = null;
        }
        
        if (debugMode)
            Debug.Log("MLManagerAdapter: Stopped continuous prediction");
    }
    
    /// <summary>
    /// API-метод, который ARMLController ожидает для установки интервала предсказаний
    /// </summary>
    public void SetPredictionInterval(float interval)
    {
        predictionInterval = Mathf.Max(0.1f, interval);
        
        if (debugMode)
            Debug.Log($"MLManagerAdapter: Set prediction interval to {predictionInterval}s");
    }
    
    /// <summary>
    /// API-метод, который ARMLController ожидает для захвата кадра
    /// </summary>
    public Texture2D CaptureCurrentFrame()
    {
        if (arCameraManager != null)
        {
            // Используем ARFoundation CPU Image API вместо ReadPixels
            if (arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                using (image)
                {
                    try
                    {
                        // Конвертируем XRCpuImage в Texture2D
                        var conversionParams = new XRCpuImage.ConversionParams
                        {
                            inputRect = new RectInt(0, 0, image.width, image.height),
                            outputDimensions = new Vector2Int(image.width / downsampleFactor, image.height / downsampleFactor),
                            outputFormat = TextureFormat.RGBA32,
                            transformation = XRCpuImage.Transformation.MirrorY
                        };
                        
                        int size = image.GetConvertedDataSize(conversionParams);
                        var buffer = new byte[size];
                        
                        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                        image.Convert(conversionParams, handle.AddrOfPinnedObject(), buffer.Length);
                        handle.Free();
                        
                        // Создаем текстуру если нужно
                        if (_currentFrameTexture == null || _currentFrameTexture.width != conversionParams.outputDimensions.x || 
                            _currentFrameTexture.height != conversionParams.outputDimensions.y)
                        {
                            if (_currentFrameTexture != null)
                                Destroy(_currentFrameTexture);
                                
                            _currentFrameTexture = new Texture2D(
                                conversionParams.outputDimensions.x,
                                conversionParams.outputDimensions.y,
                                conversionParams.outputFormat,
                                false
                            );
                        }
                        
                        // Загружаем данные напрямую в текстуру
                        _currentFrameTexture.LoadRawTextureData(buffer);
                        _currentFrameTexture.Apply();
                        
                        ProcessFrame(_currentFrameTexture);
                        return _currentFrameTexture;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Error converting CPU image to texture: {e.Message}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("Failed to acquire CPU image from AR camera");
            }
        }
        
        // Фолбэк на старый метод с WaitForEndOfFrame (он уже вызван в PredictionCoroutine)
        if (Camera.main == null)
            return null;
            
        int width = Screen.width / downsampleFactor;
        int height = Screen.height / downsampleFactor;
        
        // Создаем текстуру если нужно
        if (_currentFrameTexture == null || _currentFrameTexture.width != width || _currentFrameTexture.height != height)
        {
            if (_currentFrameTexture != null)
                Destroy(_currentFrameTexture);
                
            _currentFrameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }
        
        // Читаем пиксели (вызывается после WaitForEndOfFrame в PredictionCoroutine)
        _currentFrameTexture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
        _currentFrameTexture.Apply();
        
        ProcessFrame(_currentFrameTexture);
        return _currentFrameTexture;
    }
    
    /// <summary>
    /// Корутина для периодического захвата и обработки кадров
    /// </summary>
    private IEnumerator PredictionCoroutine()
    {
        while (_isPredicting)
        {
            try
            {
                // Attempt to capture frame and process it
                CaptureCurrentFrame();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in prediction coroutine: {e.Message}");
            }
            
            // Wait for the specified interval
            yield return new WaitForSeconds(predictionInterval);
        }
    }
    
    /// <summary>
    /// Обработка кадра через SegmentationManager
    /// </summary>
    private void ProcessFrame(Texture2D frame)
    {
        if (segmentationManager != null)
        {
            Vector2Int targetResolution = new Vector2Int(frame.width, frame.height);
            segmentationManager.ProcessCameraFrame(frame, targetResolution);
        }
        else if (deepLabPredictor != null)
        {
            deepLabPredictor.PredictSegmentation(frame);
        }
        else
        {
            Debug.LogWarning("MLManagerAdapter: Cannot process frame - missing both SegmentationManager and DeepLabPredictor");
        }
    }
    
    /// <summary>
    /// Преобразует класс в MLManager для совместимости
    /// </summary>
    public MLManager AsMlManager()
    {
        try
        {
            var mlManager = gameObject.AddComponent<MLManager>();
            return mlManager;
        }
        catch (System.Exception)
        {
            Debug.LogWarning("MLManagerAdapter: Failed to create MLManager wrapper, returning null");
            return null;
        }
    }

    /// <summary>
    /// Process the current camera frame using the ML model
    /// </summary>
    /// <param name="frame">The AR camera frame to process</param>
    /// <returns>True if processing started successfully</returns>
    public bool ProcessCameraFrame(Texture2D frame, Vector2Int targetResolution)
    {
        if (segmentationManager != null)
        {
            try
            {
                Vector2Int targetRes = new Vector2Int(frame.width, frame.height);
                return segmentationManager.ProcessCameraFrame(frame, targetRes);
            }
            catch (Exception e)
            {
                Debug.LogError($"MLManagerAdapter: Error processing frame: {e.Message}");
                return false;
            }
        }
        else if (deepLabPredictor != null)
        {
            try 
            {
                deepLabPredictor.PredictSegmentation(frame);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"MLManagerAdapter: Error in deep lab prediction: {e.Message}");
                return false;
            }
        }
        else
        {
            Debug.LogWarning("MLManagerAdapter: Cannot process frame - missing both SegmentationManager and DeepLabPredictor");
            return false;
        }
    }

    /// <summary>
    /// Обработчик события получения кадра от AR-камеры
    /// </summary>
    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        // Check if we should process this frame
        if (processOnEveryFrame || (Time.time - lastProcessingTime) >= processingInterval)
        {
            // Only try to get the latest camera image when we're ready to process
            if (cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                using (image)
                {
                    // Convert to texture and process
                    ProcessCPUImage(image);
                }
                
                lastProcessingTime = Time.time;
            }
            else
            {
                // If we can't get CPU image, try using texture coordinates instead
                // This is a fallback method
                ProcessCameraTextureCoordinates();
            }
        }
    }

    /// <summary>
    /// Processes CPU image from AR camera
    /// </summary>
    private void ProcessCPUImage(XRCpuImage image)
    {
        Texture2D cameraTexture = null;
        try
        {
            cameraTexture = ConvertCpuImageToTexture(image);
            if (cameraTexture != null)
            {
                // Define target resolution for processing
                Vector2Int targetResolution = new Vector2Int(captureWidth, captureHeight);
                
                // Forward the texture to the SegmentationManager or DeepLabPredictor
                if (segmentationManager != null)
                {
                    segmentationManager.ProcessCameraFrame(cameraTexture, targetResolution);
                }
                else if (deepLabPredictor != null)
                {
                    deepLabPredictor.PredictSegmentation(cameraTexture);
                }
                else
                {
                    Debug.LogWarning("MLManagerAdapter: Cannot process frame - missing both SegmentationManager and DeepLabPredictor");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"MLManagerAdapter: Error processing CPU image: {e.Message}");
        }
        finally
        {
            // Always clean up the temporary texture to prevent memory leaks
            if (cameraTexture != null && cameraTexture != _cameraTexture)
            {
                Destroy(cameraTexture);
                cameraTexture = null;
            }
        }
    }

    private void UpdateCameraTextureFromFrame(XRCameraFrame frame)
    {
        // This is a simplified version - in a real implementation, you would
        // use ARCameraManager.TryAcquireLatestCpuImage and convert the XRCpuImage to a texture
        
        try
        {
            // Placeholder implementation - fill with a color pattern for testing
            Color[] colors = new Color[captureWidth * captureHeight];
            for (int y = 0; y < captureHeight; y++)
            {
                for (int x = 0; x < captureWidth; x++)
                {
                    // Create a simple gradient pattern
                    colors[y * captureWidth + x] = new Color(
                        (float)x / captureWidth,
                        (float)y / captureHeight,
                        0.5f,
                        1.0f
                    );
                }
            }
            
            _cameraTexture.SetPixels(colors);
            _cameraTexture.Apply();
            
            Debug.Log("Updated camera texture from frame");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating camera texture: {e.Message}");
        }
    }

    /// <summary>
    /// Processes camera texture coordinates as a fallback when CPU image is not available
    /// </summary>
    private void ProcessCameraTextureCoordinates()
    {
        try
        {
            // Create a texture using Unity's SafeTextureReader for proper timing
            SafeTextureReader.Instance.CaptureFullScreenSafe((texture) => {
                if (texture != null)
                {
                    // Resize the texture to match the expected dimensions
                    Vector2Int targetResolution = new Vector2Int(captureWidth, captureHeight);
                    
                    // Process with SegmentationManager
                    if (segmentationManager != null)
                    {
                        segmentationManager.ProcessCameraFrame(texture, targetResolution);
                    }
                    else if (deepLabPredictor != null)
                    {
                        deepLabPredictor.PredictSegmentation(texture);
                    }
                    else
                    {
                        Debug.LogWarning("MLManagerAdapter: Cannot process texture - missing both SegmentationManager and DeepLabPredictor");
                        Destroy(texture);
                    }
                }
            });
            
            lastProcessingTime = Time.time;
        }
        catch (Exception e)
        {
            Debug.LogError($"MLManagerAdapter: Error processing texture coordinates: {e.Message}");
        }
    }

    /// <summary>
    /// Converts an XRCpuImage to a Texture2D for processing
    /// </summary>
    private Texture2D ConvertCpuImageToTexture(XRCpuImage image)
    {
        // Remove ARKit format which doesn't exist in some Unity versions
        if (image.format != XRCpuImage.Format.AndroidYuv420_888 && 
            image.format != XRCpuImage.Format.DepthFloat32)
        {
            Debug.LogWarning($"MLManagerAdapter: Potentially unsupported image format: {image.format}");
        }

        try
        {
            // Create a new texture with the target dimensions
            Texture2D texture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
            
            // Configure conversion parameters
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(captureWidth, captureHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };
            
            // Calculate buffer size and allocate memory
            int bufferSize = image.GetConvertedDataSize(conversionParams);
            byte[] buffer = new byte[bufferSize];
            
            // Pin the buffer in memory so it can't be moved
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            
            // Convert the image to RGBA format
            image.Convert(conversionParams, handle.AddrOfPinnedObject(), buffer.Length);
            
            // Unpin the buffer
            handle.Free();
            
            // Load the data into the texture
            texture.LoadRawTextureData(buffer);
            texture.Apply();
            
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogError($"MLManagerAdapter: Error converting CPU image to texture: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Handles cleanup of resources to prevent memory leaks
    /// </summary>
    private void OnDestroy()
    {
        // Ensure we stop any ongoing processing
        StopContinuousPrediction();
        
        // Clean up textures
        if (_cameraTexture != null)
        {
            Destroy(_cameraTexture);
            _cameraTexture = null;
        }
        
        if (_currentFrameTexture != null)
        {
            Destroy(_currentFrameTexture);
            _currentFrameTexture = null;
        }
    }
} 