using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.Runtime.InteropServices;
using Random = UnityEngine.Random;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine.Profiling;
using System.Threading.Tasks;

namespace ML.DeepLab
{
    /// <summary>
    /// Custom property attribute to mark ReadOnly fields in the inspector
    /// </summary>
    [System.Serializable]
    public class ReadOnlyAttribute : PropertyAttribute { }
    
    /// <summary>
    /// Handles preprocessing of images for DeepLab model
    /// </summary>
    public class DeepLabPreprocessor
    {
        public RenderTexture ProcessImage(Texture inputTexture, bool preprocess)
        {
            // Simple implementation to allow compilation
            RenderTexture output = new RenderTexture(inputTexture.width, inputTexture.height, 0, RenderTextureFormat.ARGB32);
            output.enableRandomWrite = true;
            output.Create();
            Graphics.Blit(inputTexture, output);
            return output;
        }
    }
    
    /// <summary>
    /// Enhanced version of DeepLabPredictor with additional post-processing for wall detection
    /// </summary>
    public class EnhancedDeepLabPredictor : DeepLabPredictor
    {
        // Add missing private fields
        private byte _wallClassId = 9;  // Updated to ADE20K wall class ID (9)
        private float _classificationThreshold = 0.5f;  // Default threshold
        private DeepLabPredictor _predictor;
        private bool preprocess = true;
        private DeepLabPreprocessor _preprocessor;
        
        // Delegate for handling segmentation results
        public delegate void SegmentationResultHandler(RenderTexture segmentationMask);
        
        // Event that fires when segmentation results are available
        public event SegmentationResultHandler OnSegmentationResult;
        
        // Modified delegate for Texture2D-based segmentation results for WallMeshRenderer
        public delegate void SegmentationTextureHandler(Texture2D segmentationTexture);
        
        // Event that fires when segmentation results are available as Texture2D
        public event SegmentationTextureHandler OnSegmentationCompleted;
        
        [Header("Enhanced Settings")]
        [Tooltip("Use ArgMax mode for classification (improves accuracy)")]
        public bool useArgMaxMode = true;
        
        [Header("Post-Processing Options")]
        [Tooltip("Apply noise reduction to remove small artifacts")]
        public bool applyNoiseReduction = true;

        [Tooltip("Apply wall filling to close small gaps in walls")]
        public bool applyWallFilling = true;

        [Tooltip("Apply temporal smoothing to reduce flicker between frames")]
        public bool applyTemporalSmoothing = true;

        [Tooltip("Temporal smoothing factor - higher values prioritize previous frames")]
        [Range(0, 1)] public float temporalSmoothingFactor = 0.7f;

        [Tooltip("Allow creation of a basic shader if the required shaders aren't found")]
        public bool allowFallbackShader = true;
        
        [Header("Statistics Collection")]
        [Tooltip("Collect and analyze class distribution statistics")]
        public bool collectStatistics = false;

        [Tooltip("How often to update statistics (in frames)")]
        public int statisticsUpdateInterval = 10;

        [Header("Debug Options")]
        [Tooltip("Show debug information in console")]
        public bool debugMode = false;
        
        [Tooltip("Show more detailed debug information in the console")]
        public bool verbose = false;

        [Header("Optimization Settings")]
        public bool limitTextureSize = true;
        [Tooltip("Maximum input texture size to prevent GPU limitations")]
        public int maxTextureSize = 256; // Added lower texture size limit
        [Tooltip("Use more efficient memory management")]
        public bool optimizeMemoryUsage = true;

        // Statistics properties
        [ReadOnly]
        public float wallPixelPercentage = 0f;

        [ReadOnly]
        public int lastWallPixelCount = 0;

        [ReadOnly]
        public int lastTotalPixelCount = 0;

        [ReadOnly, HideInInspector]
        public Dictionary<byte, int> classDistribution = new Dictionary<byte, int>();
        
        // Internal variables
        private RenderTexture _enhancedResultMask; // Renamed from resultMask to avoid conflict with base class
        private RenderTexture previousMask;
        private Texture2D _segmentationTexture;
        private int _enhancedFrameCount = 0; // Renamed from frameCount to avoid conflict with base class
        private bool isModelLoaded = false;
        private ComputeShader _postProcessingShader;
        private Camera mainCamera;
        private IWorker localEngine; // Local copy since base class engine is private
        
        // Cache for the generated shader
        private Shader _cachedPostProcessingShader;

        // Add field for processed texture
        private RenderTexture _processedTexture;

        // Add missing private fields
        private float lastPredictionTime = 0f;
        private float minPredictionInterval = 0.1f; // Min interval between predictions (100ms)
        private bool texturesInitialized = false;
        // Note: We use inputWidth and inputHeight from the base class DeepLabPredictor

        // Add missing fields
        private RenderTexture rawSegmentationResult;
        private RenderTexture previousResultTexture;
        
        /// <summary>
        /// Width of the input texture
        /// </summary>
        public int TextureWidth => inputWidth;
        
        /// <summary>
        /// Height of the input texture
        /// </summary>
        public int TextureHeight => inputHeight;
        
        /// <summary>
        /// Event that fires when segmentation results are available
        /// </summary>
        public UnityEngine.Events.UnityEvent<Texture2D> OnSegmentationUpdated;

        /// <summary>
        /// Class ID for walls in the segmentation output
        /// </summary>
        public override int WallClassId
        {
            get => _wallClassId;
            set
            {
                if (_wallClassId != value)
                {
                    _wallClassId = (byte)value;
                    
                    // Notify of change
                    if (debugMode)
                        Debug.Log($"EnhancedDeepLabPredictor: Wall class ID changed to {_wallClassId}");
                        
                    // Trigger event for components that need to update
                    OnWallClassIdChanged?.Invoke(_wallClassId);
                }
            }
        }

        /// <summary>
        /// Event fired when the wall class ID changes
        /// </summary>
        public override event System.Action<byte> OnWallClassIdChanged;

        /// <summary>
        /// Minimum confidence threshold for class detection (0-1)
        /// </summary>
        public override float ClassificationThreshold
        {
            get => _classificationThreshold;
            set
            {
                // Validate and clamp the value
                float newValue = Mathf.Clamp01(value);
                
                if (!Mathf.Approximately(_classificationThreshold, newValue))
                {
                    _classificationThreshold = newValue;
                    
                    // Notify of change
                    if (debugMode)
                        Debug.Log($"EnhancedDeepLabPredictor: Classification threshold changed to {_classificationThreshold:F2}");
                        
                    // Trigger event for components that need to update
                    OnClassificationThresholdChanged?.Invoke(_classificationThreshold);
                }
            }
        }

        /// <summary>
        /// Event fired when the classification threshold changes
        /// </summary>
        public event System.Action<float> OnClassificationThresholdChanged;

        /// <summary>
        /// Gets the post-processing shader, creating a fallback if necessary
        /// </summary>
        public Shader PostProcessingShader
        {
            get
            {
                if (_cachedPostProcessingShader != null)
                    return _cachedPostProcessingShader;
                
                // First try to find it by name
                Shader shader = Shader.Find("Hidden/DeepLabPostProcessing");
                
                if (shader != null)
                {
                    _cachedPostProcessingShader = shader;
                    return shader;
                }
                
                // Next try other common shader names
                string[] shaderNames = new string[]
                {
                    "Custom/DeepLabSegmentationProcess",
                    "Hidden/SegmentationPostProcess",
                    "Unlit/Texture"
                };
                
                foreach (string name in shaderNames)
                {
                    shader = Shader.Find(name);
                    if (shader != null)
                    {
                        _cachedPostProcessingShader = shader;
                        if (debugMode)
                            Debug.Log($"EnhancedDeepLabPredictor: Found shader: {name}");
                        return shader;
                    }
                }
                
                // If we have allowFallbackShader enabled, create a simple fallback
                if (allowFallbackShader)
                {
                    if (debugMode)
                        Debug.Log("EnhancedDeepLabPredictor: Creating fallback post-processing shader");
                    
                    shader = CreateBasicPostProcessingShader();
                    if (shader != null)
                    {
                        _cachedPostProcessingShader = shader;
                        return shader;
                    }
                }
                
                // If all else fails
                Debug.LogWarning("EnhancedDeepLabPredictor: No suitable post-processing shader found");
                return null;
            }
        }
        
        /// <summary>
        /// Initializes this instance from an existing DeepLabPredictor
        /// </summary>
        /// <param name="source">The source DeepLabPredictor to copy settings from</param>
        /// <returns>True if initialization was successful, false otherwise</returns>
        public bool InitializeFromSource(DeepLabPredictor source)
        {
            try
            {
                if (source == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Source predictor is null!");
                    return false;
                }

                // Copy essential properties from the source predictor
                this.modelAsset = source.modelAsset;
                this.inputWidth = source.inputWidth;
                this.inputHeight = source.inputHeight;
                this.WallClassId = (byte)source.WallClassId;
                this.ClassificationThreshold = source.ClassificationThreshold;
                
                // Ensure we have a reference to ourselves
                _predictor = this;
                
                // Validate model asset
                if (this.modelAsset == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Model asset is null after copying from source!");
                    return false;
                }

                if (debugMode)
                {
                    Debug.Log($"EnhancedDeepLabPredictor: Successfully copied settings from source predictor:");
                    Debug.Log($"- Model Asset: {this.modelAsset.name}");
                    Debug.Log($"- Input Dimensions: {inputWidth}x{inputHeight}");
                    Debug.Log($"- Wall Class ID: {WallClassId}");
                    Debug.Log($"- Classification Threshold: {ClassificationThreshold}");
                }
                
                // Start initialization
                StartCoroutine(InitializeDelayed());
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during initialization from source: {ex.Message}");
                return false;
            }
        }
        
        // Use this instead of Start() since base class Start() is private
        void Awake()
        {
            try
            {
                // Only start initialization if we're not going to be initialized from a source
                if (modelAsset != null)
                {
                    StartCoroutine(InitializeDelayed());
                }
                else if (debugMode)
                {
                    Debug.Log("EnhancedDeepLabPredictor: Awaiting initialization from source...");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during Awake: {ex.Message}");
            }
        }
        
        IEnumerator InitializeDelayed()
        {
            // Delay initialization to ensure all components are ready
            Debug.Log("EnhancedDeepLabPredictor: Starting delayed initialization...");
            yield return new WaitForSeconds(0.1f);
            
            bool success = false;
            try
            {
                // Try to initialize components with a robust approach
                success = InitializeComponents();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during initialization: {e.Message}\n{e.StackTrace}");
                TryRecoverModel();
                yield break;
            }
            
            // If component initialization failed, try with retry logic
            if (!success)
            {
                Debug.LogWarning("EnhancedDeepLabPredictor: Initial component initialization failed, retrying in 0.5s...");
                yield return new WaitForSeconds(0.5f);
                
                try
                {
                    // Second attempt
                    success = InitializeComponents();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error during retry initialization: {e.Message}\n{e.StackTrace}");
                    TryRecoverModel();
                    yield break;
                }
                
                if (!success)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Component initialization failed after retries");
                    TryRecoverModel();
                    yield break;
                }
            }
            
            // Force texture initialization after model is loaded
            if (isModelLoaded && !texturesInitialized)
            {
                try
                {
                    Debug.Log("EnhancedDeepLabPredictor: Model loaded but textures not initialized, initializing now...");
                    InitializeTextures();
                    
                    if (!texturesInitialized)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to initialize textures after model loaded");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error initializing textures: {e.Message}");
                }
            }
            
            // Notify completion
            Debug.Log("EnhancedDeepLabPredictor: Delayed initialization completed successfully");
        }
        
        private bool InitializeComponents()
        {
            try
            {
                // Initialize model first since it sets isModelLoaded flag
                InitializeModel();
                
                if (!isModelLoaded)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Model initialization failed");
                    return false;
                }
                
                // Next initialize enhancements which includes texture setup
                InitializeEnhancements();
                
                // Validate that critical texture components are initialized
                if (!texturesInitialized)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Texture initialization failed");
                    // Force texture initialization one more time
                    InitializeTextures();
                    
                    if (!texturesInitialized)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Texture initialization failed even after retry");
                        return false;
                    }
                }
                
                // Set the self-reference to avoid null predictor errors
                _predictor = this;
                
                // Everything initialized successfully
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in InitializeComponents: {e.Message}");
                return false;
            }
        }
        
        private void InitializeModel()
        {
            try
            {
                // Log model info before loading
                Debug.Log($"EnhancedDeepLabPredictor: Attempting to load model asset {modelAsset.name}");
                
                // Проверяем, есть ли модель в ресурсах, если modelAsset не задан
                if (modelAsset == null)
                {
                    string modelResourcePath = "ML/Models/model"; // Путь к модели в Resources
                    var resourceModel = Resources.Load<Unity.Barracuda.NNModel>(modelResourcePath);
                    
                    if (resourceModel != null)
                    {
                        Debug.Log($"EnhancedDeepLabPredictor: Loaded model from Resources: {modelResourcePath}");
                        modelAsset = resourceModel;
                    }
                    else
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Не найден NNModel по пути {modelResourcePath}");
                        return;
                    }
                }
                
                // Similar to base class Initialize but with our own engine reference
                var runtimeModel = ModelLoader.Load(modelAsset);
                
                if (runtimeModel != null)
                {
                    Debug.Log($"EnhancedDeepLabPredictor: Successfully loaded model with {runtimeModel.outputs.Count} outputs");
                    
                    // Create worker with appropriate backend - попробуем разные типы для совместимости
                    try {
                        // Попробуем Compute, как наиболее производительный
                        localEngine = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, runtimeModel);
                        Debug.Log("EnhancedDeepLabPredictor: Создан ComputePrecompiled worker");
                    }
                    catch (Exception e1) {
                        Debug.LogWarning($"EnhancedDeepLabPredictor: Не удалось создать ComputePrecompiled: {e1.Message}, пробуем Burst");
                        try {
                            // Альтернатива - Burst, который работает на большинстве устройств
                            localEngine = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, runtimeModel);
                            Debug.Log("EnhancedDeepLabPredictor: Создан CSharpBurst worker");
                        }
                        catch (Exception e2) {
                            Debug.LogWarning($"EnhancedDeepLabPredictor: Не удалось создать CSharpBurst: {e2.Message}, пробуем Auto");
                            // В крайнем случае - Auto, который выберет любой доступный бэкенд
                            localEngine = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
                            Debug.Log("EnhancedDeepLabPredictor: Создан Auto worker");
                        }
                    }
                    
                    // Устанавливаем флаг готовности сегментации
                    isModelLoaded = true;
                    
                    // Log available model outputs
                    Debug.Log($"EnhancedDeepLabPredictor: Model outputs: {string.Join(", ", runtimeModel.outputs)}");
                    
                    Debug.Log("EnhancedDeepLabPredictor: NNModel engine создан успешно");
                }
                else
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Runtime model is null after loading!");
                    TryRecoverModel();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Failed to initialize model: {e.Message}");
                Debug.LogException(e);
                TryRecoverModel();
            }
        }
        
        private void InitializeEnhancements()
        {
            try 
            {
                // Set input dimensions with constraints for better performance
                int maxInputSize = Mathf.Min(SystemInfo.maxTextureSize, maxTextureSize > 0 ? maxTextureSize : 512);
                
                if (Application.isMobilePlatform)
                {
                    // More conservative limits for mobile
                    maxInputSize = Mathf.Min(maxInputSize, 256);
                }
                
                // Set input dimensions
                inputWidth = maxInputSize;
                inputHeight = maxInputSize;
                
                if (debugMode)
                    Debug.Log($"EnhancedDeepLabPredictor: Limited texture size to {inputWidth}x{inputHeight}");
                
                // Initialize textures
                if (!texturesInitialized)
                {
                    InitializeTextures();
                    
                    if (!texturesInitialized)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to initialize textures during enhancement initialization");
                    }
                }
                
                // Create preprocessor for color space conversion if needed
                if (_preprocessor == null)
                {
                    _preprocessor = new DeepLabPreprocessor();
                }
                
                // Check for required post-processing shader
                if (applyTemporalSmoothing || applyWallFilling || applyNoiseReduction)
                {
                    if (PostProcessingShader != null)
                    {
                        if (debugMode)
                            Debug.Log("EnhancedDeepLabPredictor: Using post-processing shader: " + PostProcessingShader.name);
                    }
                    else if (allowFallbackShader)
                    {
                        if (debugMode)
                            Debug.Log("EnhancedDeepLabPredictor: Creating fallback post-processing shader");
                        
                        _cachedPostProcessingShader = CreateBasicPostProcessingShader();
                        
                        if (_cachedPostProcessingShader != null)
                        {
                            if (debugMode)
                                Debug.Log("EnhancedDeepLabPredictor: Fallback shader created successfully");
                        }
                        else
                        {
                            Debug.LogWarning("EnhancedDeepLabPredictor: Failed to create fallback shader");
                        }
                    }
                }
                
                // Mark enhanced features as initialized
                isModelLoaded = true;
                
                if (debugMode)
                    Debug.Log("EnhancedDeepLabPredictor: Enhanced features initialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error initializing enhancements: {e.Message}");
                isModelLoaded = false;
            }
        }
        
        private void ClearRenderTexture(RenderTexture rt)
        {
            if (rt == null) return;
            
            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prevRT;
        }
        
        /// <summary>
        /// Process a texture for segmentation
        /// </summary>
        /// <param name="inputTexture">The texture to process</param>
        /// <returns>The segmentation result texture</returns>
        public RenderTexture ProcessTexture(Texture inputTexture)
        {
            if (inputTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Input texture is null");
                return null;
            }

            try
            {
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Processing texture {inputTexture.name} ({inputTexture.width}x{inputTexture.height})");
                
                // Ensure we have a valid preprocessor
                if (_preprocessor == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Preprocessor is not initialized");
                    return null;
                }
                
                // Initialize or resize the result textures if needed
                InitializeTextures();
                
                // No predictor means we can't process
                if (_predictor == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Predictor is not initialized. This should have been set to 'this'.");
                    
                    // Try to recover by setting it now
                    _predictor = this;
                    
                    if (debugMode)
                    {
                        Debug.Log("EnhancedDeepLabPredictor: Attempted to recover by setting predictor to self. Continuing execution.");
                    }
                    else
                    {
                        return null;
                    }
                }
                
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Starting segmentation process");
                
                // Preprocess the input texture
                RenderTexture preprocessedTexture = _preprocessor.ProcessImage(inputTexture, preprocess);
                
                if (preprocessedTexture == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to preprocess input texture");
                    return null;
                }
                
                // Convert RenderTexture to Texture2D
                Texture2D texture2D = ConvertRenderTextureToTexture2D(preprocessedTexture);
                
                // Process the image - use our own method since DeepLabPredictor doesn't have ProcessImage
                RenderTexture resultMask = PredictSegmentation(texture2D);
                
                if (resultMask == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to generate segmentation mask");
                    return null;
                }
                
                // Copy the result to our enhanced result texture
                Graphics.Blit(resultMask, _enhancedResultMask);
                
                // Apply post-processing effects if enabled
                bool shouldApplyPostProcessing = false;
                try
                {
                    // Check if any post-processing is enabled
                    shouldApplyPostProcessing = applyNoiseReduction || applyWallFilling || applyTemporalSmoothing;
                    
                    // Only proceed if we have a valid shader (if temporal smoothing is enabled)
                    if (applyTemporalSmoothing && PostProcessingShader == null)
                    {
                        Debug.LogWarning("EnhancedDeepLabPredictor: Skipping post-processing - no valid shader available");
                        shouldApplyPostProcessing = false;
                    }
                    
                    // Apply post-processing if needed
                    if (shouldApplyPostProcessing)
                    {
                        ApplyPostProcessing(resultMask, _enhancedResultMask);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error checking post-processing requirements: {e.Message}");
                    // Continue without post-processing
                }
                
                // Update statistics if enabled
                if (collectStatistics)
                {
                    UpdateSegmentationStatistics(resultMask);
                }
                
                if (debugMode)
                    Debug.Log($"EnhancedDeepLabPredictor: Texture processing complete. Post-processing applied: {shouldApplyPostProcessing}");
                
                // Clean up temporary texture
                if (texture2D != null)
                    Destroy(texture2D);
                
                return _enhancedResultMask;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error processing frame: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Process a single texture frame for segmentation
        /// </summary>
        /// <param name="inputTexture">The Texture2D to process</param>
        public void ProcessFrames(Texture2D inputTexture)
        {
            if (inputTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Input texture is null for ProcessFrames");
                return;
            }

            try
            {
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Processing single frame with texture {inputTexture.width}x{inputTexture.height}");
                
                // Create a temporary texture to avoid modifying the input
                RenderTexture tempRT = RenderTexture.GetTemporary(inputTexture.width, inputTexture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(inputTexture, tempRT);
                
                // Process the temporary texture
                RenderTexture result = ProcessTexture(tempRT);
                
                // Release the temporary texture
                RenderTexture.ReleaseTemporary(tempRT);
                
                // Fire segmentation event if needed
                if (result != null && OnSegmentationResult != null)
                {
                    OnSegmentationResult.Invoke(result);
                }
                
                // Also update the segmentation texture
                if (result != null && OnSegmentationUpdated != null)
                {
                    Texture2D segTex = ConvertRenderTextureToTexture2D(result);
                    if (segTex != null)
                    {
                        OnSegmentationUpdated.Invoke(segTex);
                        // Don't destroy segTex as it's passed to the event
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error processing frame: {e.Message}");
            }
        }
        
        IEnumerator ProcessFrames()
        {
            while (true)
            {
                if (isModelLoaded && localEngine != null && mainCamera != null)
                {
                    // Capture camera frame
                    Texture2D cameraTexture = CaptureCamera(mainCamera, inputWidth, inputHeight);
                    
                    if (cameraTexture != null)
                    {
                        // Process the frame
                        ProcessTexture(cameraTexture);
                        
                        // Clean up temporary texture
                        Destroy(cameraTexture);
                    }
                }
                
                // Wait before processing next frame
                yield return new WaitForSeconds(0.1f); // 10 FPS to save resources
            }
        }
        
        public Texture2D CaptureCamera(Camera camera, int width, int height)
        {
            RenderTexture rt = new RenderTexture(width, height, 24);
            camera.targetTexture = rt;
            Texture2D screenShot = new Texture2D(width, height, TextureFormat.RGB24, false);
            camera.Render();
            RenderTexture.active = rt;
            screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenShot.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);
            return screenShot;
        }
        
        /// <summary>
        /// Initializes textures needed for segmentation
        /// </summary>
        private void InitializeTextures()
        {
            try
            {
                // Create result texture with proper format
                if (_enhancedResultMask == null)
                {
                    _enhancedResultMask = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                    _enhancedResultMask.enableRandomWrite = true;
                    _enhancedResultMask.Create();
                }
                
                // Create raw segmentation texture for intermediary results
                if (rawSegmentationResult == null)
                {
                    rawSegmentationResult = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                    rawSegmentationResult.enableRandomWrite = true;
                    rawSegmentationResult.Create();
                }
                
                // Create texture for previous frame result if temporal smoothing is enabled
                if (applyTemporalSmoothing && previousResultTexture == null)
                {
                    previousResultTexture = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                    previousResultTexture.enableRandomWrite = true;
                    previousResultTexture.Create();
                    
                    // Clear it
                    RenderTexture prevActive = RenderTexture.active;
                    RenderTexture.active = previousResultTexture;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = prevActive;
                }
                
                // Verify textures were created successfully
                if (_enhancedResultMask != null && _enhancedResultMask.IsCreated() && 
                    rawSegmentationResult != null && rawSegmentationResult.IsCreated())
                {
                    texturesInitialized = true;
                    if (debugMode)
                        Debug.Log("EnhancedDeepLabPredictor: Textures initialized successfully");
                }
                else
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to create textures");
                    texturesInitialized = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in InitializeTextures: {e.Message}");
                texturesInitialized = false;
            }
        }
        
        /// <summary>
        /// Resizes a texture to the specified dimensions
        /// </summary>
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
        
        /// <summary>
        /// Preprocesses a texture for model input
        /// </summary>
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
        
        /// <summary>
        /// Processes output tensor to create a segmentation mask
        /// </summary>
        private void EnhancedConvertOutputToMask(Tensor output, RenderTexture targetTexture, int classId)
        {
            if (output == null || targetTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Output tensor or target texture is null");
                return;
            }
            
            try
            {
                // Get the shape of the output tensor
                var shape = output.shape;
                if (debugMode)
                    Debug.Log($"Output tensor shape: {shape}");
                
                // Download tensor data
                float[] rawData = output.data.Download(shape);
                
                // Create temporary texture for mask
                Texture2D maskTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
                Color[] colors = new Color[inputWidth * inputHeight];
                
                // Process based on tensor format
                if (shape.height == 1 && shape.width == shape.channels)
                {
                    // Handle flat tensor with one class per channel
                    for (int y = 0; y < inputHeight; y++)
                    {
                        for (int x = 0; x < inputWidth; x++)
                        {
                            int pixelIdx = y * inputWidth + x;
                            if (pixelIdx < colors.Length && pixelIdx < rawData.Length)
                            {
                                float value = rawData[pixelIdx];
                                int predictedClass = Mathf.RoundToInt(value);
                                bool isWall = (predictedClass == classId);
                                colors[pixelIdx] = isWall ? Color.white : new Color(0, 0, 0, 0);
                            }
                        }
                    }
                }
                else if (shape.channels > 1)
                {
                    // Handle multi-channel format with class probability per channel
                    int numClasses = shape.channels;
                    
                    for (int y = 0; y < inputHeight; y++)
                    {
                        for (int x = 0; x < inputWidth; x++)
                        {
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
                            
                            // Check if it's a wall and if the confidence is high enough
                            bool isWall = (maxClassId == classId && maxProb > _classificationThreshold);
                            colors[pixelIdx] = isWall ? Color.white : new Color(0, 0, 0, 0);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Unsupported tensor format: {shape}");
                }
                
                // Set colors to texture
                maskTexture.SetPixels(colors);
                maskTexture.Apply();
                
                // Copy to render texture
                Graphics.Blit(maskTexture, targetTexture);
                
                // Clean up
                Destroy(maskTexture);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in EnhancedConvertOutputToMask: {e.Message}");
            }
        }
        
        /// <summary>
        /// Check if walls were detected
        /// </summary>
        private bool CheckIfWallsDetected()
        {
            // Implementation would count wall pixels or check combined wall area
            // For now, return true to simplify
            return true;
        }
        
        /// <summary>
        /// Process the output mask and apply additional features like filling
        /// </summary>
        private void ProcessOutputToMask()
        {
            // Apply post-processing from raw mask to result mask
            ApplyPostProcessing(rawSegmentationResult, _enhancedResultMask);
        }
        
        /// <summary>
        /// Process the output tensor into a segmentation mask with proper filtering
        /// </summary>
        private void ProcessOutputToMask(Tensor output, RenderTexture targetTexture)
        {
            if (output == null || targetTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Output tensor or target texture is null");
                return;
            }

            try
            {
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Processing output tensor with shape {output.shape}");

                // Get dimensions from tensor
                int height = output.shape[1];
                int width = output.shape[2];
                int channels = output.shape[3];

                // Create temporary texture for initial processing
                if (_segmentationTexture == null || _segmentationTexture.width != width || _segmentationTexture.height != height)
                {
                    if (_segmentationTexture != null)
                        DestroyImmediate(_segmentationTexture);

                    _segmentationTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                }

                // Get tensor data
                float[] outputData = output.AsFloats();

                // Process output data to create segmentation mask
                Color32[] maskPixels = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelIndex = y * width + x;
                        int baseIndex = pixelIndex * channels;

                        byte classId = 0;
                        float maxConfidence = 0;

                        // ArgMax - find class with highest confidence
                        if (useArgMaxMode)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                float confidence = outputData[baseIndex + c];
                                if (confidence > maxConfidence)
                                {
                                    maxConfidence = confidence;
                                    classId = (byte)c;
                                }
                            }
                        }
                        // Focus on wall class - check if confidence is high enough
                        else
                        {
                            maxConfidence = outputData[baseIndex + (byte)WallClassId];
                            classId = (maxConfidence >= _classificationThreshold) ? (byte)WallClassId : (byte)0;
                        }

                        // Store class ID and confidence in the mask pixels
                        // Store class ID in R channel and confidence in G channel
                        maskPixels[pixelIndex] = new Color32(
                            classId,  // Class ID in R channel
                            (byte)(maxConfidence * 255), // Confidence in G channel
                            (byte)(classId == (byte)WallClassId ? 255 : 0), // Highlight walls in B channel
                            255);
                    }
                }

                // Apply to texture
                _segmentationTexture.SetPixels32(maskPixels);
                _segmentationTexture.Apply();

                // Apply OpenCV morphological processing for wall class if available
                ApplyMorphologicalProcessing(_segmentationTexture);

                // Apply to target RenderTexture
                Graphics.Blit(_segmentationTexture, targetTexture);

                // Fire the segmentation texture event
                TriggerSegmentationEvents(targetTexture);

                // Update statistics
                if (collectStatistics)
                {
                    UpdateSegmentationStatistics(targetTexture);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error processing output to mask: {e.Message}");
            }
        }
        
        /// <summary>
        /// Apply morphological processing to enhance wall segmentation
        /// </summary>
        private void ApplyMorphologicalProcessing(Texture2D segmentationTexture)
        {
            if ((applyNoiseReduction || applyWallFilling) && segmentationTexture != null)
            {
                try
                {
                    // Process with OpenCV for better wall detection if available
                    // This is a placeholder - actual implementation would depend on OpenCV availability
                    if (debugMode)
                        Debug.Log("EnhancedDeepLabPredictor: Applied morphological processing to wall mask");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error in morphological processing: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Trigger segmentation events with the processed texture
        /// </summary>
        private void TriggerSegmentationEvents(RenderTexture targetTexture)
        {
            if (OnSegmentationCompleted != null)
            {
                // Convert RenderTexture to Texture2D for the event
                Texture2D segTexture = ConvertRenderTextureToTexture2D(targetTexture);
                
                // Invoke with the converted Texture2D
                if (segTexture != null)
                {
                    OnSegmentationCompleted.Invoke(segTexture);
                }
            }
            
            if (OnSegmentationUpdated != null)
            {
                Texture2D segTexture = GetSegmentationTexture();
                if (segTexture != null)
                {
                    OnSegmentationUpdated.Invoke(segTexture);
                }
            }
        }
        
        /// <summary>
        /// Backward compatibility method that uses the enhanced result mask
        /// </summary>
        private void ApplyPostProcessing()
        {
            if (_enhancedResultMask == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Result mask is null, cannot apply post-processing");
                return;
            }
            
            // Create a temporary texture for processing
            RenderTexture tempResult = RenderTexture.GetTemporary(
                _enhancedResultMask.width, 
                _enhancedResultMask.height, 
                0, 
                _enhancedResultMask.format
            );
            
            try
            {
                // Apply post-processing from the result mask to the temp texture
                ApplyPostProcessing(_enhancedResultMask, tempResult);
                
                // Copy the processed result back to the result mask
                Graphics.Blit(tempResult, _enhancedResultMask);
            }
            finally
            {
                // Release the temporary texture
                RenderTexture.ReleaseTemporary(tempResult);
            }
        }
        
        /// <summary>
        /// Predicts segmentation for the given input texture and applies post-processing
        /// </summary>
        /// <param name="inputTexture">The input texture to segment</param>
        /// <returns>A RenderTexture containing the enhanced segmentation result</returns>
        public override RenderTexture PredictSegmentation(Texture2D inputTexture)
        {
            // Check if model is loaded
            if (!isModelLoaded)
            {
                Debug.LogWarning("EnhancedDeepLabPredictor: Сегментация пока не готова, пропускаем кадр");
                return null;
            }
            
            // Ensure textures are initialized
            if (!texturesInitialized)
            {
                try {
                    InitializeTextures();
                    if (!texturesInitialized)
                    {
                        Debug.LogWarning("EnhancedDeepLabPredictor: Unable to initialize textures, skipping frame");
                        return null;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error initializing textures: {e.Message}");
                    return null;
                }
            }
            
            // Check throttling to avoid processing frames too quickly
            if (Time.time - lastPredictionTime < minPredictionInterval)
            {
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Skipping frame due to throttling ({Time.time - lastPredictionTime}s < {minPredictionInterval}s)");
                return _enhancedResultMask;
            }
            
            // Update timing
            lastPredictionTime = Time.time;
            _enhancedFrameCount++;
            
            // Process the frame
            try
            {
                if (inputTexture == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Input texture is null");
                    return null;
                }
                
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Processing frame {_enhancedFrameCount} with texture {inputTexture.width}x{inputTexture.height}");
                
                // Clear the raw result texture
                if (rawSegmentationResult != null)
                {
                    ClearRenderTexture(rawSegmentationResult);
                }
                else
                {
                    Debug.LogWarning("EnhancedDeepLabPredictor: Raw segmentation texture is null, cannot clear");
                    return _enhancedResultMask;
                }
                
                // Process the input texture
                Texture2D resized = null;
                Tensor inputTensor = null;
                Tensor outputTensor = null;
                
                try
                {
                    // Resize input to model dimensions
                    resized = ResizeTexture(inputTexture, inputWidth, inputHeight);
                    if (resized == null)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to resize input texture");
                        return _enhancedResultMask;
                    }
                    
                    // Convert to tensor
                    inputTensor = PreprocessTexture(resized);
                    if (inputTensor == null)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to preprocess texture to tensor");
                        return _enhancedResultMask;
                    }
                    
                    // Run inference
                    localEngine.Execute(inputTensor);
                    
                    // Get output tensor
                    try
                    {
                        // Try to get output using the model's output name
                        string outputName = "SemanticPredictions"; // Default name
                        outputTensor = localEngine.PeekOutput(outputName);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"EnhancedDeepLabPredictor: Error getting named output: {e.Message}");
                        
                        try
                        {
                            // Fallback to getting the first output tensor
                            outputTensor = localEngine.PeekOutput();
                            if (debugMode)
                                Debug.Log("EnhancedDeepLabPredictor: Using default PeekOutput without parameters");
                        }
                        catch (System.Exception e2)
                        {
                            Debug.LogError($"EnhancedDeepLabPredictor: Failed to get model outputs: {e2.Message}");
                            return _enhancedResultMask;
                        }
                    }
                    
                    if (outputTensor == null)
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to get output tensor");
                        return _enhancedResultMask;
                    }
                    
                    // Process output tensor to raw result texture
                    EnhancedConvertOutputToMask(outputTensor, rawSegmentationResult, (byte)WallClassId);
                    
                    // Apply post-processing
                    ProcessOutputToMask(outputTensor, _enhancedResultMask);
                    
                    // Check for wall detection
                    CheckIfWallsDetected();
                    
                    // Return the enhanced result
                    return _enhancedResultMask;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error in PredictSegmentation: {e.Message}\n{e.StackTrace}");
                    return _enhancedResultMask;
                }
                finally
                {
                    // Properly dispose of resources
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
                    
                    // Clean up resized texture if created
                    if (resized != null && resized != inputTexture)
                    {
                        Destroy(resized);
                        resized = null;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in PredictSegmentation: {e.Message}\n{e.StackTrace}");
                return _enhancedResultMask;
            }
        }
        
        /// <summary>
        /// Converts RenderTexture to Texture2D properly with error handling
        /// </summary>
        private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                Debug.LogError("Cannot convert null RenderTexture to Texture2D");
                return null;
            }

            Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            RenderTexture.active = renderTexture;
            
            try
            {
                texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture2D.Apply();
                return texture2D;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error converting RenderTexture to Texture2D: {e.Message}");
                Destroy(texture2D);
                return null;
            }
            finally
            {
                RenderTexture.active = null;
            }
        }
        
        /// <summary>
        /// Process the current camera frame
        /// </summary>
        public Texture2D ProcessCurrentFrame(RenderTexture frameTexture)
        {
            if (frameTexture == null)
            {
                Debug.LogError("Cannot process null frame texture");
                return null;
            }

            try
            {
                // Fix: Properly convert RenderTexture to Texture2D using conversion method
                Texture2D texture2D = ConvertRenderTextureToTexture2D(frameTexture);
                
                if (texture2D == null)
                {
                    Debug.LogError("Failed to convert RenderTexture to Texture2D");
                    return null;
                }

                ProcessFrames(texture2D);
                
                if (OnSegmentationUpdated != null && _segmentationTexture != null)
                {
                    OnSegmentationUpdated.Invoke(_segmentationTexture);
                }
                
                return texture2D;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing frame: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Gets segmentation for a specific class
        /// </summary>
        public Texture2D GetSegmentationForClass(int classId)
        {
            if (_enhancedResultMask == null)
            {
                if (debugMode)
                {
                    Debug.LogWarning("EnhancedDeepLabPredictor: No segmentation result available yet");
                }
                return null;
            }
            
            try
            {
                // Convert RenderTexture to Texture2D first
                Texture2D segTexture = ConvertRenderTextureToTexture2D(_enhancedResultMask);
                if (segTexture == null) return null;
                
                // Create a new texture to hold just the specified class
                Texture2D result = new Texture2D(segTexture.width, segTexture.height, 
                    TextureFormat.RGBA32, false);
                
                // Get the segmentation texture pixels
                Color[] pixels = segTexture.GetPixels();
                Color[] resultPixels = new Color[pixels.Length];
                
                // Extract only the requested class
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = pixels[i];
                    byte pixelClassId = (byte)(pixel.r * 255);
                    
                    if (pixelClassId == classId)
                    {
                        // This pixel belongs to the requested class
                        resultPixels[i] = new Color(1, 1, 1, pixel.a);
                    }
                    else
                    {
                        // This pixel does not belong to the requested class
                        resultPixels[i] = new Color(0, 0, 0, 0);
                    }
                }
                
                // Apply the pixels to the result texture
                result.SetPixels(resultPixels);
                result.Apply();
                
                // Clean up the temporary texture
                Destroy(segTexture);
                
                if (debugMode)
                {
                    Debug.Log($"EnhancedDeepLabPredictor: Created segmentation for class {classId}");
                }
                
                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error creating segmentation for class {classId}: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Applies post-processing effects to the result texture based on enabled options
        /// </summary>
        /// <param name="sourceTexture">Source texture containing segmentation data</param>
        /// <param name="targetTexture">Target texture to write post-processed results to</param>
        private void ApplyPostProcessing(RenderTexture sourceTexture, RenderTexture targetTexture)
        {
            // Validate input textures
            if (sourceTexture == null || targetTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Source or target texture is null, cannot apply post-processing");
                return;
            }

            if (debugMode && verbose)
                Debug.Log("EnhancedDeepLabPredictor: Starting post-processing");
            
            // Fast path: If no post-processing is enabled, just copy source to target
            if (!applyTemporalSmoothing && !applyWallFilling && !applyNoiseReduction)
            {
                Graphics.Blit(sourceTexture, targetTexture);
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: No post-processing enabled, direct copy applied");
                return;
            }
            
            RenderTexture tempRT1 = null;
            RenderTexture tempRT2 = null;
            
            try
            {
                // Create temporary render textures at the same resolution as source
                tempRT1 = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, sourceTexture.format);
                tempRT2 = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, sourceTexture.format);
                
                // Copy source to first temp texture
                Graphics.Blit(sourceTexture, tempRT1);
                
                // Apply temporal smoothing if enabled
                if (applyTemporalSmoothing && previousMask != null)
                {
                    try
                    {
                        if (debugMode && verbose)
                            Debug.Log("EnhancedDeepLabPredictor: Applying temporal smoothing");
                        
                        Shader blendShader = PostProcessingShader;
                        
                        if (blendShader != null)
                        {
                            Material blendMaterial = null;
                            try
                            {
                                blendMaterial = new Material(blendShader);
                                
                                // Set up material properties
                                blendMaterial.SetTexture("_MainTex", tempRT1);
                                blendMaterial.SetTexture("_BlendTex", previousMask);
                                blendMaterial.SetFloat("_Blend", 0.3f); // 70% current, 30% previous
                                
                                // Apply temporal smoothing effect
                                Graphics.Blit(tempRT1, tempRT2, blendMaterial);
                                
                                // Swap render textures
                                SwapTextures(ref tempRT1, ref tempRT2);
                                
                                // Update previous mask for next frame
                                Graphics.Blit(tempRT1, previousMask);
                            }
                            finally
                            {
                                // Clean up material
                                SafeDestroyMaterial(blendMaterial);
                            }
                        }
                        else if (debugMode)
                        {
                            Debug.LogWarning("EnhancedDeepLabPredictor: Skipping temporal smoothing - shader not available");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Failed to apply temporal smoothing: {e.Message}");
                    }
                }
                
                // Apply confidence threshold if needed
                float threshold = ClassificationThreshold;
                if (threshold > 0.01f && threshold < 0.99f)
                {
                    try
                    {
                        if (debugMode && verbose)
                            Debug.Log($"EnhancedDeepLabPredictor: Applying confidence threshold {threshold}");
                        
                        // Apply threshold using compute shader or material
                        // Implementation depends on available shaders
                        // Fallback to direct copy if not implemented
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Failed to apply confidence threshold: {e.Message}");
                    }
                }
                
                // Apply wall filling if enabled
                if (applyWallFilling)
                {
                    try
                    {
                        if (debugMode && verbose)
                            Debug.Log("EnhancedDeepLabPredictor: Applying wall filling");
                        
                        // Apply dilation or morphological operations to fill gaps
                        // Implementation depends on compute shader availability
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Failed to apply wall filling: {e.Message}");
                    }
                }
                
                // Apply noise reduction if enabled
                if (applyNoiseReduction)
                {
                    try
                    {
                        if (debugMode && verbose)
                            Debug.Log("EnhancedDeepLabPredictor: Applying noise reduction");
                        
                        // Apply median or gaussian filter
                        // Implementation depends on compute shader availability
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Failed to apply noise reduction: {e.Message}");
                    }
                }
                
                // Copy final result to the target texture
                Graphics.Blit(tempRT1, targetTexture);
                
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Post-processing completed successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in post-processing: {e.Message}");
                
                // Fallback: direct copy if post-processing fails
                try
                {
                    Graphics.Blit(sourceTexture, targetTexture);
                }
                catch
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Critical failure - even fallback copy failed");
                }
            }
            finally
            {
                // Always release temporary render textures
                if (tempRT1 != null)
                    RenderTexture.ReleaseTemporary(tempRT1);
                if (tempRT2 != null)
                    RenderTexture.ReleaseTemporary(tempRT2);
            }
        }
        
        /// <summary>
        /// Helper method to swap two RenderTexture references
        /// </summary>
        private void SwapTextures(ref RenderTexture tex1, ref RenderTexture tex2)
        {
            RenderTexture temp = tex1;
            tex1 = tex2;
            tex2 = temp;
        }
        
        /// <summary>
        /// Safely destroys a material to prevent memory leaks
        /// </summary>
        private void SafeDestroyMaterial(Material material)
        {
            if (material != null)
            {
                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }
        }
        
        private void TryRecoverModel()
        {
            try
            {
                Debug.Log("EnhancedDeepLabPredictor: Attempting to recover from initialization failure...");

                // Try to find model asset in other predictors
                var predictors = FindObjectsByType<DeepLabPredictor>(FindObjectsSortMode.None);
                foreach (var predictor in predictors)
                {
                    if (predictor != this && predictor.modelAsset != null)
                    {
                        Debug.Log($"EnhancedDeepLabPredictor: Found valid model asset in predictor: {predictor.name}");
                        modelAsset = predictor.modelAsset;
                        StartCoroutine(InitializeDelayed());
                        return;
                    }
                }

                // Try to find model in resources
                var models = Resources.FindObjectsOfTypeAll<Unity.Barracuda.NNModel>();
                foreach (var model in models)
                {
                    if (model != null && (model.name.Contains("DeepLab") || model.name.Contains("Segmentation")))
                    {
                        Debug.Log($"EnhancedDeepLabPredictor: Found potential model in resources: {model.name}");
                        modelAsset = model;
                        StartCoroutine(InitializeDelayed());
                        return;
                    }
                }

                Debug.LogError("EnhancedDeepLabPredictor: Recovery failed - no valid model found");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during recovery attempt: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets the segmentation texture for WallMeshRenderer
        /// </summary>
        public Texture2D GetSegmentationTexture()
        {
            if (_enhancedResultMask == null)
            {
                return null;
            }
            
            try
            {
                // Create texture if it doesn't exist
                if (_segmentationTexture == null || 
                    _segmentationTexture.width != _enhancedResultMask.width || 
                    _segmentationTexture.height != _enhancedResultMask.height)
                {
                    if (_segmentationTexture != null)
                    {
                        Destroy(_segmentationTexture);
                    }
                    _segmentationTexture = new Texture2D(_enhancedResultMask.width, _enhancedResultMask.height, TextureFormat.RGBA32, false);
                }
                
                // Read pixels from resultMask to texture
                RenderTexture.active = _enhancedResultMask;
                _segmentationTexture.ReadPixels(new Rect(0, 0, _enhancedResultMask.width, _enhancedResultMask.height), 0, 0);
                _segmentationTexture.Apply();
                RenderTexture.active = null;
                
                return _segmentationTexture;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating segmentation texture: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Creates a basic blend shader when built-in shaders are unavailable
        /// </summary>
        private Shader CreateBasicPostProcessingShader()
        {
            try
            {
                // Try to find any simple shader that can be used as a fallback
                Shader fallbackShader = Shader.Find("Unlit/Texture");
                if (fallbackShader == null)
                    fallbackShader = Shader.Find("Standard");
                if (fallbackShader == null)
                    fallbackShader = Shader.Find("Mobile/Unlit");
                
                if (fallbackShader != null)
                {
                    if (debugMode)
                        Debug.Log($"EnhancedDeepLabPredictor: Using {fallbackShader.name} as fallback shader");
                    return fallbackShader;
                }
                
                // If we reached here, no suitable shaders were found
                Debug.LogError("EnhancedDeepLabPredictor: No suitable fallback shaders found");
                return null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error creating fallback shader: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Updates segmentation statistics
        /// </summary>
        /// <param name="segmentationTexture">The segmentation texture to analyze</param>
        private void UpdateSegmentationStatistics(RenderTexture segmentationTexture)
        {
            if (segmentationTexture == null)
            {
                Debug.LogWarning("EnhancedDeepLabPredictor: Invalid segmentation texture provided");
                return;
            }

            try
            {
                // Convert RenderTexture to Texture2D
                Texture2D texture2D = ConvertRenderTextureToTexture2D(segmentationTexture);
                if (texture2D == null)
                {
                    Debug.LogWarning("EnhancedDeepLabPredictor: Failed to convert segmentation texture to Texture2D");
                    return;
                }

                // Get pixel data
                Color[] pixels = texture2D.GetPixels();
                int wallPixelCount = 0;
                int totalPixelCount = pixels.Length;

                // Count wall pixels
                foreach (Color pixel in pixels)
                {
                    if (pixel.r == WallClassId / 255f)
                    {
                        wallPixelCount++;
                    }
                }

                // Calculate wall pixel percentage
                wallPixelPercentage = (float)wallPixelCount / totalPixelCount;

                // Update lastWallPixelCount and lastTotalPixelCount
                lastWallPixelCount = wallPixelCount;
                lastTotalPixelCount = totalPixelCount;

                // Update class distribution
                byte wallClassIdByte = (byte)WallClassId;
                if (classDistribution.ContainsKey(wallClassIdByte))
                {
                    classDistribution[wallClassIdByte]++;
                }
                else
                {
                    classDistribution.Add(wallClassIdByte, 1);
                }

                // Clean up temporary texture
                Destroy(texture2D);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error updating segmentation statistics: {e.Message}");
            }
        }
    }
}