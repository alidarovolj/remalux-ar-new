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
        _cameraTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
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
            // Ждем конец кадра перед захватом изображения
            yield return new WaitForEndOfFrame();
            
            // Захватываем текущий кадр с камеры
            CaptureCurrentFrame();
            
            #if UNITY_EDITOR
            Debug.Log("MLManagerAdapter: Captured and processed frame");
            #endif
            
            // Ждем указанный интервал
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
    public bool ProcessCameraFrame(XRCameraFrame frame)
    {
        if (segmentationManager == null)
        {
            Debug.LogWarning("No SegmentationManager assigned to MLManagerAdapter");
            return false;
        }

        if (!processOnEveryFrame && Time.time - lastProcessingTime < processingInterval)
        {
            return false;
        }

        if (_cameraTexture == null)
        {
            _cameraTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
        }
        
        UpdateCameraTextureFromFrame(frame);
        
        if (_cameraTexture != null)
        {
            // Process the camera texture with the segmentation manager
            segmentationManager.ProcessTexture(_cameraTexture);
        }
        lastProcessingTime = Time.time;
        return true;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!processOnEveryFrame && Time.time - lastProcessingTime < processingInterval)
            return;
            
        lastProcessingTime = Time.time;
        
        // ARCameraFrameEventArgs doesn't have a cameraFrame property
        // We need to use the camera manager to get the latest frame
        if (arCameraManager != null && arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            using (cpuImage)
            {
                ProcessCameraFrame(new XRCameraFrame());
            }
        }
        else
        {
            // Fallback to texture-based processing
            CaptureCurrentFrame();
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

    // Implement a proper conversion from XRCpuImage to Texture2D in a real application
    // This would replace the placeholder implementation above
    private bool TryConvertCpuImageToTexture(XRCpuImage cpuImage, ref Texture2D texture)
    {
        if (texture == null)
        {
            texture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
        }
        
        // Your conversion code would go here
        
        // For a complete implementation, look at the ARFoundation samples from Unity:
        // https://github.com/Unity-Technologies/arfoundation-samples/blob/main/Assets/Scripts/CpuImageSample.cs
        
        return true;
    }
} 