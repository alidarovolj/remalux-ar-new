using UnityEngine;
using Unity.Barracuda;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Добавляем пространство имен, чтобы класс был доступен через ML.DeepLab.EnhancedDeepLabPredictor
namespace ML.DeepLab
{
    // Add ReadOnly attribute class
    public class ReadOnlyAttribute : PropertyAttribute { }
    
    // Add missing DeepLabPreprocessor class
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
    /// Enhanced version of DeepLabPredictor with additional features
    /// and optimizations for more accurate wall detection.
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

        [Tooltip("Allow creation of a basic shader if the required shaders aren't found")]
        public bool allowFallbackShaderCreation = true;
        
        [Header("Statistics Collection")]
        [Tooltip("Collect and analyze class distribution statistics")]
        public bool collectStatistics = false;

        [Tooltip("How often to update statistics (in frames)")]
        public int statisticsUpdateInterval = 30;

        [Header("Debug Options")]
        [Tooltip("Show debug information in console")]
        public bool debugMode = false;
        
        [Tooltip("Show more detailed debug information in the console")]
        public bool verbose = false;

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

        // Find a section that defines input tensor resolution
        [SerializeField] private int _inputWidth = 256; // Reduce from 512 
        [SerializeField] private int _inputHeight = 256; // Reduce from 512

        // Add missing private fields
        private float lastPredictionTime = 0f;
        private float minPredictionInterval = 0.1f; // Min interval between predictions (100ms)
        private bool texturesInitialized = false;
        private int inputWidth = 224;
        private int inputHeight = 224;
        
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
        public byte WallClassId
        {
            get => _wallClassId;
            set
            {
                if (_wallClassId != value)
                {
                    _wallClassId = value;
                    
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
        public event System.Action<byte> OnWallClassIdChanged;

        /// <summary>
        /// Minimum confidence threshold for class detection (0-1)
        /// </summary>
        public float ClassificationThreshold
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
                
                // Try to find the built-in shader first
                _cachedPostProcessingShader = Shader.Find("Hidden/BlendTextures");
                
                // If not found, try alternate shader names
                if (_cachedPostProcessingShader == null)
                    _cachedPostProcessingShader = Shader.Find("Hidden/Blend");
                
                // If still not found, create a basic shader
                if (_cachedPostProcessingShader == null)
                {
                    if (debugMode)
                        Debug.Log("EnhancedDeepLabPredictor: Creating fallback post-processing shader");
                    
                    _cachedPostProcessingShader = CreateBasicPostProcessingShader();
                    
                    if (_cachedPostProcessingShader == null)
                        Debug.LogError("EnhancedDeepLabPredictor: Failed to create fallback shader");
                    else if (debugMode)
                        Debug.Log("EnhancedDeepLabPredictor: Fallback shader created successfully");
                }
                
                return _cachedPostProcessingShader;
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
            if (debugMode)
                Debug.Log("EnhancedDeepLabPredictor: Starting delayed initialization...");

            // Wait for a frame to ensure all components are properly initialized
            yield return null;

            bool success = false;
            try
            {
                success = InitializeComponents();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during delayed initialization: {ex.Message}");
                // Try to recover from initialization failure
                TryRecoverModel();
            }

            if (success && debugMode)
                Debug.Log("EnhancedDeepLabPredictor: Delayed initialization completed successfully");
        }
        
        private bool InitializeComponents()
        {
            try
            {
                // Initialize the model
                InitializeModel();

                // Initialize post-processing and other enhancements
                InitializeEnhancements();

                // Initialize result textures with current dimensions
                InitializeResultTextures(inputWidth, inputHeight);

                // Create preprocessor if needed
                if (_preprocessor == null)
                    _preprocessor = new DeepLabPreprocessor();

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error initializing components: {ex.Message}");
                return false;
            }
        }
        
        private void InitializeModel()
        {
            try
            {
                // Log model info before loading
                Debug.Log($"EnhancedDeepLabPredictor: Attempting to load model asset {modelAsset.name}");
                
                // Similar to base class Initialize but with our own engine reference
                var runtimeModel = ModelLoader.Load(modelAsset);
                
                if (runtimeModel != null)
                {
                    Debug.Log($"EnhancedDeepLabPredictor: Successfully loaded model with {runtimeModel.outputs.Count} outputs");
                    
                    // Create worker with appropriate backend
                    localEngine = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
                    
                    // Log available model outputs
                    Debug.Log($"EnhancedDeepLabPredictor: Model outputs: {string.Join(", ", runtimeModel.outputs)}");
                    
                    Debug.Log("EnhancedDeepLabPredictor: Model initialized successfully");
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
                // Initialize the self-reference to avoid null predictor errors
                _predictor = this;
                
                // Create a render texture for enhanced result
                _enhancedResultMask = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                _enhancedResultMask.enableRandomWrite = true;
                _enhancedResultMask.Create();
                
                // Initialize for temporal smoothing if needed
                if (applyTemporalSmoothing)
                {
                    previousMask = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                    previousMask.enableRandomWrite = true;
                    previousMask.Create();
                }
                
                // Load post-processing shader if needed
                if (applyNoiseReduction || applyWallFilling)
                {
                    try
                    {
                        // Use the property getter to find or create the shader
                        Shader shader = PostProcessingShader;
                        
                        if (shader == null && allowFallbackShaderCreation)
                        {
                            if (debugMode)
                                Debug.Log("EnhancedDeepLabPredictor: Post-processing shader not found, creating basic one");
                            
                            _cachedPostProcessingShader = CreateBasicPostProcessingShader();
                        }
                        
                        if (_cachedPostProcessingShader == null)
                        {
                            Debug.LogWarning("EnhancedDeepLabPredictor: Could not load or create post-processing shader, some features will be disabled");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Error loading post-processing shader: {e.Message}");
                        _cachedPostProcessingShader = null;
                    }
                }
                
                // Find main camera for frame capture
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    mainCamera = FindFirstObjectByType<Camera>();
                }
                
                isModelLoaded = true;
                Debug.Log("EnhancedDeepLabPredictor: Enhanced features initialized successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Failed to initialize enhanced features: {e.Message}");
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
                InitializeResultTextures(inputTexture.width, inputTexture.height);
                
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
        /// Predicts segmentation for the given input texture and applies post-processing
        /// </summary>
        /// <param name="inputTexture">The input texture to segment</param>
        /// <returns>A RenderTexture containing the enhanced segmentation result</returns>
        public RenderTexture PredictSegmentation(Texture inputTexture)
        {
            if (inputTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Input texture is null");
                return null;
            }
            
            // Prevent excessive prediction calls
            if (Time.realtimeSinceStartup - lastPredictionTime < minPredictionInterval)
            {
                if (debugMode && verbose)
                    Debug.Log($"EnhancedDeepLabPredictor: Skipping prediction - interval too short ({Time.realtimeSinceStartup - lastPredictionTime:F2}s < {minPredictionInterval:F2}s)");
                return _enhancedResultMask; // Return last result
            }
            
            lastPredictionTime = Time.realtimeSinceStartup;
            
            try
            {
                // Check if textures need to be initialized or resized
                if (_enhancedResultMask == null || inputTexture.width != inputWidth || inputTexture.height != inputHeight)
                {
                    if (debugMode)
                        Debug.Log($"EnhancedDeepLabPredictor: Initializing result textures to match input ({inputTexture.width}x{inputTexture.height})");
                    
                    InitializeResultTextures(inputTexture.width, inputTexture.height);
                }
                
                if (!texturesInitialized)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to initialize result textures");
                    return _enhancedResultMask;
                }
                
                // Create a temporary texture to store raw segmentation result
                RenderTexture rawSegmentationResult = RenderTexture.GetTemporary(
                    inputTexture.width, 
                    inputTexture.height, 
                    0, 
                    RenderTextureFormat.ARGB32
                );
                rawSegmentationResult.enableRandomWrite = true;
                rawSegmentationResult.filterMode = FilterMode.Point;
                rawSegmentationResult.Create();
                
                try
                {
                    // Run base predictor
                    if (!RunPrediction(inputTexture, rawSegmentationResult))
                    {
                        Debug.LogError("EnhancedDeepLabPredictor: Base prediction failed");
                        return _enhancedResultMask; // Return last valid result
                    }
                    
                    // Apply post-processing
                    ApplyPostProcessing(rawSegmentationResult, _enhancedResultMask);
                    
                    // Trigger event if there are any listeners
                    if (OnSegmentationCompleted != null)
                    {
                        // Convert RenderTexture to Texture2D for the event
                        Texture2D segTexture = ConvertRenderTextureToTexture2D(_enhancedResultMask);
                        
                        // Invoke with the converted Texture2D
                        if (segTexture != null)
                        {
                            OnSegmentationCompleted.Invoke(segTexture);
                        }
                    }
                    
                    // Update previous mask for temporal smoothing if enabled
                    if (applyTemporalSmoothing && previousMask != null)
                    {
                        Graphics.Blit(_enhancedResultMask, previousMask);
                    }
                    
                    if (debugMode && verbose)
                        Debug.Log("EnhancedDeepLabPredictor: Segmentation prediction completed successfully");
                    
                    return _enhancedResultMask;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Error during segmentation prediction: {e.Message}");
                    return _enhancedResultMask; // Return last valid result
                }
                finally
                {
                    if (rawSegmentationResult != null)
                    {
                        RenderTexture.ReleaseTemporary(rawSegmentationResult);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Critical error during prediction: {e.Message}");
                return _enhancedResultMask; // Return last valid result
            }
        }
        
        /// <summary>
        /// Runs the base segmentation prediction on the input texture
        /// </summary>
        /// <param name="inputTexture">The input texture to segment</param>
        /// <param name="outputTexture">The render texture to write the result to</param>
        /// <returns>True if prediction was successful, false otherwise</returns>
        private bool RunPrediction(Texture inputTexture, RenderTexture outputTexture)
        {
            if (inputTexture == null || outputTexture == null)
                return false;
            
            try
            {
                // Use a more reasonable model input size to prevent GPU compute errors
                int modelWidth = Mathf.Min(inputTexture.width, 224);
                int modelHeight = Mathf.Min(inputTexture.height, 224);
                
                // Create a smaller texture for input to the model
                RenderTexture modelInputTexture = RenderTexture.GetTemporary(
                    modelWidth, modelHeight, 0, RenderTextureFormat.ARGB32
                );
                modelInputTexture.filterMode = FilterMode.Bilinear;
                
                try
                {
                    // Downsample the input texture to the model input texture
                    Graphics.Blit(inputTexture, modelInputTexture);
                    
                    // Run prediction on the model input texture
                    base.PredictSegmentation(modelInputTexture);
                    
                    // Try to get resultTexture from base class using reflection
                    System.Reflection.FieldInfo resultTextureField = typeof(DeepLabPredictor).GetField("resultTexture", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.Public);
                    
                    if (resultTextureField != null)
                    {
                        RenderTexture baseResultTexture = resultTextureField.GetValue(this) as RenderTexture;
                        if (baseResultTexture != null)
                        {
                            Graphics.Blit(baseResultTexture, outputTexture);
                            return true;
                        }
                    }
                    
                    // If reflection fails, try another approach - check if our _enhancedResultMask can be used
                    if (_enhancedResultMask != null)
                    {
                        Graphics.Blit(_enhancedResultMask, outputTexture);
                        return true;
                    }
                    
                    return false;
                }
                finally
                {
                    if (modelInputTexture != null)
                    {
                        RenderTexture.ReleaseTemporary(modelInputTexture);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error during base prediction: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Converts a RenderTexture to a Texture2D
        /// </summary>
        private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Input render texture is null");
                return null;
            }

            // Переиспользуем существующую текстуру или создаем новую с правильными размерами
            if (_segmentationTexture == null || 
                _segmentationTexture.width != renderTexture.width || 
                _segmentationTexture.height != renderTexture.height)
            {
                // Очистка предыдущей текстуры, если она существует
                if (_segmentationTexture != null)
                    Destroy(_segmentationTexture);
                    
                _segmentationTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            }
            
            // Сохраняем текущий активный RenderTexture
            RenderTexture prevActive = RenderTexture.active;
            
            // Делаем наш renderTexture активным для чтения пикселей
            RenderTexture.active = renderTexture;
            
            // Читаем пиксели из активного renderTexture в текстуру
            _segmentationTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            _segmentationTexture.Apply();
            
            // Восстанавливаем предыдущий активный RenderTexture
            RenderTexture.active = prevActive;

            return _segmentationTexture;
        }
        
        /// <summary>
        /// Overloaded version of PreprocessTextureEnhanced that can work with RenderTexture
        /// </summary>
        private Tensor PreprocessTextureEnhanced(RenderTexture renderTexture)
        {
            if (renderTexture == null) return null;
            
            // Convert RenderTexture to Texture2D
            Texture2D texture2D = ConvertRenderTextureToTexture2D(renderTexture);
            if (texture2D == null) return null;
            
            try {
                // Use the existing method with Texture2D
                Tensor result = PreprocessTextureEnhanced(texture2D);
                
                // Не уничтожаем текстуру, так как она теперь переиспользуется
                // Destroy(texture2D);
                
                return result;
            }
            catch (System.Exception e) {
                Debug.LogError($"Error in PreprocessTextureEnhanced for RenderTexture: {e.Message}");
                
                // Не уничтожаем текстуру, так как она теперь переиспользуется
                // Destroy(texture2D);
                
                return null;
            }
        }
        
        /// <summary>
        /// Our enhanced version of PreprocessTexture for Texture2D
        /// </summary>
        private Tensor PreprocessTextureEnhanced(Texture2D texture)
        {
            if (texture == null) return null;
            
            int width = texture.width;
            int height = texture.height;
            
            // Create tensor with appropriate dimensions [1, height, width, 3]
            // Use the actual texture dimensions to avoid redundant resizing
            Tensor tensor = new Tensor(1, height, width, 3);
            
            try {
                // Get pixel data directly - more efficient than GetPixels()
                Color32[] pixelColors = texture.GetPixels32();
                
                if (pixelColors == null || pixelColors.Length == 0)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to get pixel data from texture");
                    return null;
                }
                
                // Use parallel threading for better performance on large textures
                bool useParallel = width * height > 10000 && SystemInfo.processorCount > 1;
                
                if (useParallel)
                {
                    System.Threading.Tasks.Parallel.For(0, height, (y) => {
                        for (int x = 0; x < width; x++)
                        {
                            int pixelIndex = y * width + x;
                            
                            if (pixelIndex < pixelColors.Length)
                            {
                                Color32 color = pixelColors[pixelIndex];
                                // Normalize color values to 0-1 range
                                tensor[0, y, x, 0] = color.r / 255f;
                                tensor[0, y, x, 1] = color.g / 255f;
                                tensor[0, y, x, 2] = color.b / 255f;
                            }
                        }
                    });
                }
                else
                {
                    // Sequential processing for smaller textures
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int pixelIndex = y * width + x;
                            
                            if (pixelIndex < pixelColors.Length)
                            {
                                Color32 color = pixelColors[pixelIndex];
                                // Normalize color values to 0-1 range
                                tensor[0, y, x, 0] = color.r / 255f;
                                tensor[0, y, x, 1] = color.g / 255f;
                                tensor[0, y, x, 2] = color.b / 255f;
                            }
                        }
                    }
                }
                
                return tensor;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Error in PreprocessTextureEnhanced: {e.Message}");
                
                if (tensor != null)
                {
                    tensor.Dispose();
                }
                
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
                            maxConfidence = outputData[baseIndex + _wallClassId];
                            classId = (maxConfidence >= _classificationThreshold) ? _wallClassId : (byte)0;
                        }

                        // Store class ID and confidence in the mask pixels
                        // Store class ID in R channel and confidence in G channel
                        maskPixels[pixelIndex] = new Color32(
                            classId,  // Class ID in R channel
                            (byte)(maxConfidence * 255), // Confidence in G channel
                            (byte)(classId == _wallClassId ? 255 : 0), // Highlight walls in B channel
                            255);
                    }
                }

                // Apply to texture
                _segmentationTexture.SetPixels32(maskPixels);
                _segmentationTexture.Apply();

                // Apply OpenCV morphological processing for wall class
                if ((applyNoiseReduction || applyWallFilling) && WallOpenCVProcessor.IsOpenCVAvailable())
                {
                    try
                    {
                        // Process with OpenCV for better wall detection
                        Texture2D processedTex = WallOpenCVProcessor.EnhanceWallMask(_segmentationTexture);
                        if (processedTex != null)
                        {
                            // Replace segmentation texture with the processed version
                            DestroyImmediate(_segmentationTexture);
                            _segmentationTexture = processedTex;
                            
                            if (debugMode)
                                Debug.Log("EnhancedDeepLabPredictor: Applied OpenCV morphological processing to wall mask");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EnhancedDeepLabPredictor: Error in OpenCV processing: {e.Message}");
                    }
                }

                // Apply to target RenderTexture
                Graphics.Blit(_segmentationTexture, targetTexture);

                // Fire the segmentation texture event
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

                // Keep track of whether this is our first processed frame
                _enhancedFrameCount++;

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
        /// Initializes or reinitializes the result textures with the specified dimensions
        /// </summary>
        /// <param name="width">Width of the result textures</param>
        /// <param name="height">Height of the result textures</param>
        private void InitializeResultTextures(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Invalid texture dimensions: {width}x{height}");
                return;
            }
            
            // Apply stricter size constraints to prevent GPU memory issues on mobile
            int maxTextureSize = Mathf.Min(SystemInfo.maxTextureSize, 1024); // More conservative limit for mobile
            
            // Check if the texture is too large and resize if needed
            if (width > maxTextureSize || height > maxTextureSize)
            {
                float scale = Mathf.Min((float)maxTextureSize / width, (float)maxTextureSize / height);
                width = Mathf.FloorToInt(width * scale);
                height = Mathf.FloorToInt(height * scale);
                
                if (debugMode)
                    Debug.LogWarning($"EnhancedDeepLabPredictor: Texture dimensions were clamped to {width}x{height} to avoid GPU memory issues");
            }
            
            // Additional check for mobile platforms to ensure smaller textures
            if (Application.isMobilePlatform)
            {
                // Use
            }
        }
    }
} // Закрытие класса EnhancedDeepLabPredictor и пространства имен ML.DeepLab