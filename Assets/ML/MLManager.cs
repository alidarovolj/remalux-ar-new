using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Interface class to handle ML model management and inference
/// </summary>
public class MLManager : MonoBehaviour
{
    [Header("Prediction Settings")]
    public float predictionInterval = 0.5f;
    public bool runPredictionOnStart = true;
    public int downsampleFactor = 2;
    public bool useEnhancedPredictor = true;
    
    [Header("Wall Detection")]
    public byte wallClassId = 9; // ADE20K wall class ID
    
    [SerializeField] private DeepLabPredictor _predictor;
    private DeepLabPredictor _enhancedPredictor;
    
    [SerializeField] private ARCameraManager arCameraManager;
    
    private Coroutine _predictionCoroutine;
    private bool _isPredicting = false;
    private Texture2D _frameTexture;
    
    // Event for segmentation completion
    public event Action<RenderTexture> OnSegmentationComplete;
    
    [SerializeField] protected NNModel modelAsset;
    [SerializeField] protected WorkerFactory.Type workerType = WorkerFactory.Type.Auto;
    [SerializeField] protected bool debugMode = false;
    
    protected Model runtimeModel;
    protected IWorker worker;
    protected bool isInitialized = false;
    
    public bool IsInitialized => isInitialized;
    
    private void OnEnable() 
    {
        if (arCameraManager != null) {
            arCameraManager.frameReceived += OnCameraFrameReceived;
        }
    }
    
    private void OnDisable() 
    {
        if (arCameraManager != null) {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }
    
    private void OnCameraFrameReceived(ARCameraFrameEventArgs args) 
    {
        if (!_isPredicting || args.textures.Count == 0)
            return;
            
        // Process the camera frame from ARFoundation
        Texture2D frameTexture = ConvertARCameraFrameToTexture2D(args.textures[0]);
        
        if (frameTexture != null) 
        {
            // Use enhanced predictor if available
            if (_enhancedPredictor != null && useEnhancedPredictor) 
            {
                _enhancedPredictor.PredictSegmentation(frameTexture);
            } 
            else 
            {
                _predictor.PredictSegmentation(frameTexture);
            }
        }
    }
    
    private Texture2D ConvertARCameraFrameToTexture2D(Texture cameraTexture) 
    {
        if (cameraTexture == null)
            return null;
            
        // Calculate dimensions with downsample factor
        int width = cameraTexture.width / downsampleFactor;
        int height = cameraTexture.height / downsampleFactor;
        
        // Mobile device optimizations
        if (Application.isMobilePlatform) 
        {
            int maxSize = Application.platform == RuntimePlatform.IPhonePlayer ? 224 : 384;
            if (width > maxSize || height > maxSize) 
            {
                float scale = Mathf.Min((float)maxSize / width, (float)maxSize / height);
                width = Mathf.FloorToInt(width * scale);
                height = Mathf.FloorToInt(height * scale);
            }
        }
        
        // Create texture if needed
        if (_frameTexture == null || _frameTexture.width != width || _frameTexture.height != height) 
        {
            if (_frameTexture != null)
                Destroy(_frameTexture);
                
            _frameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }
        
        // Convert AR camera frame to Texture2D
        RenderTexture tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(cameraTexture, tempRT);
        
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = tempRT;
        
        _frameTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        _frameTexture.Apply();
        
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(tempRT);
        
        return _frameTexture;
    }
    
    private void HandleSegmentationResult(RenderTexture segmentationMask)
    {
        // Forward the event to our subscribers
        OnSegmentationComplete?.Invoke(segmentationMask);
    }
    
    private void Start()
    {
        if (runPredictionOnStart)
            StartPrediction();
            
        // Verify the model is correct
        VerifyModelAsset();
    }
    
    private void Awake()
    {
        if (_predictor == null)
        {
            // Попытка найти DeepLabPredictor автоматически
            _predictor = FindFirstObjectByType<DeepLabPredictor>();
            
            if (_predictor == null)
            {
                Debug.LogError("MLManager: DeepLabPredictor not assigned!");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("MLManager: Automatically found and assigned DeepLabPredictor");
            }
        }
        
        // Find ARCameraManager if not assigned
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager == null)
            {
                Debug.LogWarning("MLManager: ARCameraManager not found, frame capture may not work on iOS");
            }
        }
        
        // Initialize predictors and set wall class ID
        if (useEnhancedPredictor) 
        {
            _enhancedPredictor = GetComponent<DeepLabPredictor>();
            
            // Если не нашли на этом объекте, попробуем найти в сцене
            if (_enhancedPredictor == null)
            {
                // Ищем Enhanced предиктор особым способом, т.к. нам неизвестно точное имя класса
                DeepLabPredictor[] allPredictors = FindObjectsOfType<DeepLabPredictor>();
                foreach (var predictor in allPredictors)
                {
                    if (predictor.GetType().Name.Contains("Enhanced"))
                    {
                        _enhancedPredictor = predictor;
                        Debug.Log("MLManager: Found Enhanced DeepLab predictor in scene");
                        break;
                    }
                }
            }
            
            if (_enhancedPredictor != null) 
            {
                _enhancedPredictor.WallClassId = wallClassId;
                
                // Attempt to subscribe to events via reflection to avoid namespace issues
                var type = _enhancedPredictor.GetType();
                var eventInfo = type.GetEvent("OnSegmentationResult");
                if (eventInfo != null) 
                {
                    var delegateType = eventInfo.EventHandlerType;
                    var handler = Delegate.CreateDelegate(delegateType, this, 
                        typeof(MLManager).GetMethod("HandleSegmentationResult", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                    eventInfo.AddEventHandler(_enhancedPredictor, handler);
                }
            }
        }
        
        // Ensure predictor has the right wall class ID
        _predictor.WallClassId = wallClassId;
    }
    
    public void StartPrediction()
    {
        if (_isPredicting)
            return;
            
        _isPredicting = true;
        
        // Only start coroutine if we're not using ARCameraManager
        if (arCameraManager == null) 
        {
            _predictionCoroutine = StartCoroutine(PredictionLoop());
        }
        
        #if UNITY_EDITOR
        Debug.Log("MLManager: Started prediction loop");
        #endif
    }
    
    public void StopPrediction()
    {
        _isPredicting = false;
        
        if (_predictionCoroutine != null)
        {
            StopCoroutine(_predictionCoroutine);
            _predictionCoroutine = null;
        }
        
        #if UNITY_EDITOR
        Debug.Log("MLManager: Stopped prediction loop");
        #endif
    }
    
    private IEnumerator PredictionLoop()
    {
        // Only used as fallback when ARCameraManager is not available
        while (_isPredicting)
        {
            Texture2D frameTexture = CaptureCurrentFrame();
            
            if (frameTexture != null)
            {
                if (_enhancedPredictor != null && useEnhancedPredictor)
                {
                    _enhancedPredictor.PredictSegmentation(frameTexture);
                }
                else
                {
                    _predictor.PredictSegmentation(frameTexture);
                }
            }
            
            yield return new WaitForSeconds(predictionInterval);
        }
    }
    
    // Legacy method kept for backward compatibility
    private Texture2D CaptureCurrentFrame()
    {
        // Find AR Camera's texture
        if (Camera.main != null && Camera.main.targetTexture != null)
        {
            // Calculation and conversion code here as before
            // [Existing implementation]
            int width = Camera.main.targetTexture.width / downsampleFactor;
            int height = Camera.main.targetTexture.height / downsampleFactor;
            
            if (Application.isMobilePlatform)
            {
                int maxSize = Application.platform == RuntimePlatform.IPhonePlayer ? 224 : 384;
                if (width > maxSize || height > maxSize)
                {
                    float scale = Mathf.Min((float)maxSize / width, (float)maxSize / height);
                    width = Mathf.FloorToInt(width * scale);
                    height = Mathf.FloorToInt(height * scale);
                }
            }
            
            if (_frameTexture == null || _frameTexture.width != width || _frameTexture.height != height)
            {
                if (_frameTexture != null)
                    Destroy(_frameTexture);
                    
                _frameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = Camera.main.targetTexture;
            
            _frameTexture.ReadPixels(new Rect(0, 0, Camera.main.targetTexture.width, Camera.main.targetTexture.height), 0, 0, false);
            _frameTexture.Apply();
            
            RenderTexture.active = currentRT;
            
            return _frameTexture;
        }
        
        Debug.LogWarning("MLManager: Unable to capture frame - no camera target texture found");
        return null;
    }
    
    // Helper methods for ML controller
    public void SetPredictionInterval(float interval)
    {
        predictionInterval = Mathf.Max(0.1f, interval);
    }
    
    public void StartContinuousPrediction()
    {
        StartPrediction();
    }
    
    public void StopContinuousPrediction()
    {
        StopPrediction();
    }
    
    private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
    {
        if (renderTexture == null) return null;
        
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = renderTexture;
        
        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();
        
        RenderTexture.active = prevActive;
        return texture2D;
    }
    
    private void OnDestroy()
    {
        StopPrediction();
        
        if (_frameTexture != null)
        {
            Destroy(_frameTexture);
            _frameTexture = null;
        }
        
        Shutdown();
    }
    
    // Check if the predictor is using the correct model asset
    private void VerifyModelAsset()
    {
        if (_predictor != null)
        {
            // Check if the model asset name contains "model" (indicating model.onnx)
            var modelField = _predictor.GetType().GetField("modelAsset", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
            if (modelField != null)
            {
                var modelAsset = modelField.GetValue(_predictor) as NNModel;
                if (modelAsset != null && !modelAsset.name.Contains("model"))
                {
                    Debug.LogWarning($"MLManager: DeepLabPredictor is using '{modelAsset.name}' but 'model.onnx' is the only fully supported model for this project. This may cause issues with wall detection and segmentation. Please update references to use model.onnx.");
                }
            }
        }
        
        if (_enhancedPredictor != null && _enhancedPredictor != _predictor)
        {
            // Check enhanced predictor model as well
            var modelField = _enhancedPredictor.GetType().GetField("modelAsset", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
            if (modelField != null)
            {
                var modelAsset = modelField.GetValue(_enhancedPredictor) as NNModel;
                if (modelAsset != null && !modelAsset.name.Contains("model"))
                {
                    Debug.LogWarning($"MLManager: EnhancedDeepLabPredictor is using '{modelAsset.name}' but 'model.onnx' is the only fully supported model for this project. This may cause issues with wall detection and segmentation. Please update references to use model.onnx.");
                }
            }
        }
    }
    
    public virtual void Initialize()
    {
        if (modelAsset == null)
        {
            Debug.LogError("MLManager: Model asset is not assigned!");
            return;
        }
        
        try
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = WorkerFactory.CreateWorker(workerType, runtimeModel);
            isInitialized = true;
            
            if (debugMode)
                Debug.Log("MLManager: Initialized successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MLManager: Failed to initialize model: {e.Message}");
            isInitialized = false;
        }
    }
    
    public virtual void Process(Texture2D inputTexture)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("MLManager: Cannot process - not initialized");
            return;
        }
        
        // Base implementation would process the texture
        if (debugMode)
            Debug.Log("MLManager: Processing input texture");
    }
    
    public virtual void Shutdown()
    {
        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }
        
        isInitialized = false;
        
        if (debugMode)
            Debug.Log("MLManager: Shut down");
    }
}







