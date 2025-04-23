using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;

// Add a directive to access EnhancedDeepLabPredictor
// Even though they are in the same assembly, we need to be explicit about dependencies
// Since EnhancedDeepLabPredictor is defined in Assets/ML/DeepLab/EnhancedDeepLabPredictor.cs, using it directly

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
    private EnhancedDeepLabPredictor _enhancedPredictor;
    private Coroutine _predictionCoroutine;
    private bool _isPredicting = false;
    private Texture2D _frameTexture;
    
    // Event for segmentation completion - matches signature required by ARMLController
    public event Action<RenderTexture> OnSegmentationComplete;
    
    private void Awake()
    {
        if (_predictor == null)
        {
            Debug.LogError("MLManager: DeepLabPredictor not assigned!");
            enabled = false;
            return;
        }
        
        // Initialize the enhanced predictor if available and enabled
        if (useEnhancedPredictor)
        {
            _enhancedPredictor = GetComponent<EnhancedDeepLabPredictor>();
            if (_enhancedPredictor == null)
            {
                _enhancedPredictor = gameObject.AddComponent<EnhancedDeepLabPredictor>();
                if (_enhancedPredictor != null)
                {
                    // Copy settings from base predictor
                    _enhancedPredictor.modelAsset = _predictor.modelAsset;
                    _enhancedPredictor.WallClassId = wallClassId;
                    _enhancedPredictor.ClassificationThreshold = _predictor.ClassificationThreshold;
                    Debug.Log("MLManager: Created EnhancedDeepLabPredictor");
                    
                    // Subscribe to segmentation events
                    _enhancedPredictor.OnSegmentationResult += HandleSegmentationResult;
                }
            }
            
            if (_enhancedPredictor != null)
            {
                _enhancedPredictor.WallClassId = wallClassId;
                Debug.Log($"MLManager: Using EnhancedDeepLabPredictor with wall class ID: {wallClassId}");
                
                // Subscribe to segmentation events if we didn't already
                _enhancedPredictor.OnSegmentationResult += HandleSegmentationResult;
            }
        }
        
        // Ensure predictor has the right wall class ID
        _predictor.WallClassId = wallClassId;
    }
    
    private void Start()
    {
        if (runPredictionOnStart)
            StartPrediction();
    }
    
    private void OnDestroy()
    {
        StopPrediction();
        
        if (_frameTexture != null)
        {
            Destroy(_frameTexture);
            _frameTexture = null;
        }
        
        // Unsubscribe from events
        if (_enhancedPredictor != null)
        {
            _enhancedPredictor.OnSegmentationResult -= HandleSegmentationResult;
        }
    }
    
    private void HandleSegmentationResult(RenderTexture segmentationMask)
    {
        // Forward the event to our subscribers
        OnSegmentationComplete?.Invoke(segmentationMask);
    }
    
    // Method to set the prediction interval - needed by ARMLController
    public void SetPredictionInterval(float interval)
    {
        predictionInterval = Mathf.Max(0.1f, interval);
        Debug.Log($"MLManager: Prediction interval set to {predictionInterval}s");
    }
    
    // Method to start continuous prediction - needed by ARMLController
    public void StartContinuousPrediction()
    {
        StartPrediction();
    }
    
    // Method to stop continuous prediction - needed by ARMLController
    public void StopContinuousPrediction()
    {
        StopPrediction();
    }
    
    public void StartPrediction()
    {
        if (_isPredicting)
            return;
            
        _isPredicting = true;
        _predictionCoroutine = StartCoroutine(PredictionLoop());
        Debug.Log("MLManager: Started prediction loop");
    }
    
    public void StopPrediction()
    {
        _isPredicting = false;
        
        if (_predictionCoroutine != null)
        {
            StopCoroutine(_predictionCoroutine);
            _predictionCoroutine = null;
            Debug.Log("MLManager: Stopped prediction loop");
        }
    }
    
    private IEnumerator PredictionLoop()
    {
        while (_isPredicting)
        {
            Texture2D frameTexture = CaptureCurrentFrame();
            
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
            
            yield return new WaitForSeconds(predictionInterval);
        }
    }
    
    private Texture2D CaptureCurrentFrame()
    {
        // Get the current camera texture
        WebCamTexture camTexture = null;
        
        // Find AR Camera's texture
        if (Camera.main != null && Camera.main.targetTexture != null)
        {
            // Calculate appropriate dimensions based on the downsample factor
            int width = Camera.main.targetTexture.width / downsampleFactor;
            int height = Camera.main.targetTexture.height / downsampleFactor;
            
            // Ensure texture dimensions don't exceed GPU limits on mobile
            if (Application.isMobilePlatform)
            {
                int maxSize = Application.platform == RuntimePlatform.IPhonePlayer ? 224 : 384;
                if (width > maxSize || height > maxSize)
                {
                    float scale = Mathf.Min((float)maxSize / width, (float)maxSize / height);
                    width = Mathf.FloorToInt(width * scale);
                    height = Mathf.FloorToInt(height * scale);
                    Debug.Log($"MLManager: Texture size limited to {width}x{height} for mobile compatibility");
                }
            }
            
            // Create texture if needed or if dimensions changed
            if (_frameTexture == null || _frameTexture.width != width || _frameTexture.height != height)
            {
                if (_frameTexture != null)
                    Destroy(_frameTexture);
                    
                _frameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            
            // Read pixels from the camera's render target
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = Camera.main.targetTexture;
            
            // Read the pixels at a reduced resolution to save processing time
            _frameTexture.ReadPixels(new Rect(0, 0, Camera.main.targetTexture.width, Camera.main.targetTexture.height), 0, 0, false);
            _frameTexture.Apply();
            
            RenderTexture.active = currentRT;
            
            return _frameTexture;
        }
        
        Debug.LogWarning("MLManager: Unable to capture frame - no camera target texture found");
        return null;
    }
}







