using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;
using System.Collections;

/// <summary>
/// Адаптер для совместимости между ARMLController и SegmentationManager
/// </summary>
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
    
    private Texture2D _currentFrameTexture;
    private Coroutine _predictionCoroutine;
    private bool _isPredicting = false;
    
    private void Awake()
    {
        FindMissingReferences();
    }
    
    private void OnEnable()
    {
        // Автозапуск предсказаний при включении
        if (autoStartPrediction)
            StartContinuousPrediction();
    }
    
    private void OnDisable()
    {
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
        if (arCameraManager == null)
        {
            Debug.LogWarning("MLManagerAdapter: Cannot capture frame - missing ARCameraManager");
            return null;
        }
        
        // Захват текущего кадра через ARCameraManager
        if (_currentFrameTexture == null)
        {
            _currentFrameTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        }
        
        try
        {
            // Захват текущего кадра с экрана как запасной вариант
            _currentFrameTexture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            _currentFrameTexture.Apply();
            
            ProcessFrame(_currentFrameTexture);
            
            return _currentFrameTexture;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MLManagerAdapter: Error capturing frame: {e.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Корутина для периодического захвата и обработки кадров
    /// </summary>
    private IEnumerator PredictionCoroutine()
    {
        while (_isPredicting)
        {
            yield return new WaitForSeconds(predictionInterval);
            
            Texture2D frame = CaptureCurrentFrame();
            if (frame != null && debugMode)
                Debug.Log("MLManagerAdapter: Captured and processed frame");
        }
    }
    
    /// <summary>
    /// Обработка кадра через SegmentationManager
    /// </summary>
    private void ProcessFrame(Texture2D frame)
    {
        if (segmentationManager != null)
        {
            segmentationManager.ProcessCameraFrame(frame);
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
} 