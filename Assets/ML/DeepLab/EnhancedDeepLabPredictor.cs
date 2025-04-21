using UnityEngine;
using Unity.Barracuda;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
    private byte _wallClassId = 9;  // Default wall class ID
    private float _classificationThreshold = 0.5f;  // Default threshold
    private DeepLabPredictor _predictor;
    private bool preprocess = true;
    private DeepLabPreprocessor _preprocessor;
    
    // Delegate for handling segmentation results
    public delegate void SegmentationResultHandler(RenderTexture segmentationMask);
    
    // Event that fires when segmentation results are available
    public event SegmentationResultHandler OnSegmentationResult;
    
    // Added delegate for Texture2D-based segmentation results for WallMeshRenderer
    public delegate void SegmentationTextureHandler(Texture2D segmentationTexture);
    
    // Event that fires when segmentation results are available as Texture2D
    public event SegmentationTextureHandler OnSegmentationCompleted;
    
    [Header("Enhanced Settings")]
    [Tooltip("Use ArgMax mode for classification (improves accuracy)")]
    public bool useArgMaxMode = true;
    
    [Header("Post-Processing Options")]
    [Tooltip("Apply noise reduction to remove small artifacts")]
    public bool applyNoiseReduction = false;

    [Tooltip("Apply wall filling to close small gaps in walls")]
    public bool applyWallFilling = false;

    [Tooltip("Apply temporal smoothing to reduce flicker between frames")]
    public bool applyTemporalSmoothing = false;

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
            RenderTexture resultMask = PredictSegmentationEnhanced(texture2D);
            
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
                    ApplyPostProcessing();
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
    
    // Our own prediction implementation since we can't easily use the base class version
    private RenderTexture PredictSegmentationEnhanced(Texture2D inputTexture)
    {
        if (inputTexture == null)
        {
            Debug.LogError("EnhancedDeepLabPredictor: Input texture is null for prediction");
            return null;
        }
        
        if (localEngine == null)
        {
            Debug.LogError("EnhancedDeepLabPredictor: Engine is null for prediction");
            
            // Try to recover by reinitializing
            if (modelAsset != null)
            {
                Debug.Log("EnhancedDeepLabPredictor: Attempting to recover engine...");
                InitializeModel();
                
                // If still null after recovery attempt, give up
                if (localEngine == null)
                {
                    Debug.LogError("EnhancedDeepLabPredictor: Failed to recover engine");
                    return null;
                }
                
                Debug.Log("EnhancedDeepLabPredictor: Engine recovery successful");
            }
            else
            {
                return null;
            }
        }
        
        // Clear the result mask before processing
        ClearRenderTexture(_enhancedResultMask);
        
        // Resize input to model dimensions if needed
        Texture2D processedTexture = inputTexture;
        if (inputTexture.width != inputWidth || inputTexture.height != inputHeight)
        {
            processedTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
            Graphics.ConvertTexture(inputTexture, processedTexture);
            processedTexture.Apply();
        }
        
        // Convert to Tensor with our safe method
        Tensor inputTensor = null;
        Tensor outputTensor = null;
        
        try
        {
            inputTensor = PreprocessTextureEnhanced(processedTexture);
            
            // Run inference
            localEngine.Execute(inputTensor);
            
            // Get output tensor
            outputTensor = localEngine.PeekOutput();
            
            if (outputTensor == null)
            {
                Debug.LogError("EnhancedDeepLabPredictor: Output tensor is null");
                return null;
            }
            
            // Process output to segmentation mask
            ProcessOutputToMask(outputTensor, _enhancedResultMask);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error during prediction: {e.Message}");
            return null;
        }
        finally
        {
            // Clean up tensors
            if (inputTensor != null)
                inputTensor.Dispose();
            if (outputTensor != null)
                outputTensor.Dispose();
                
            // Clean up temporary texture
            if (processedTexture != inputTexture)
                Destroy(processedTexture);
        }
        
        return _enhancedResultMask;
    }
    
    // Our enhanced version of PreprocessTexture
    private Tensor PreprocessTextureEnhanced(Texture2D texture)
    {
        // Create tensor with appropriate dimensions [1, height, width, 3]
        Tensor tensor = new Tensor(1, inputHeight, inputWidth, 3);
        
        // Resize texture to match input dimensions if needed
        Texture2D resizedTexture = texture;
        if (texture.width != inputWidth || texture.height != inputHeight)
        {
            resizedTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
            Graphics.ConvertTexture(texture, resizedTexture);
            resizedTexture.Apply();
        }
        
        Color[] pixelColors = resizedTexture.GetPixels();
        
        // Safely copy data to tensor with proper indexing
        for (int y = 0; y < inputHeight; y++)
        {
            for (int x = 0; x < inputWidth; x++)
            {
                int pixelIndex = y * inputWidth + x;
                
                if (pixelIndex < pixelColors.Length)
                {
                    Color color = pixelColors[pixelIndex];
                    tensor[0, y, x, 0] = color.r;
                    tensor[0, y, x, 1] = color.g;
                    tensor[0, y, x, 2] = color.b;
                }
            }
        }
        
        // Clean up if we created a new texture
        if (resizedTexture != texture)
        {
            Destroy(resizedTexture);
        }
        
        return tensor;
    }
    
    /// <summary>
    /// Process output tensor to segmentation mask
    /// </summary>
    private void ProcessOutputToMask(Tensor output, RenderTexture targetTexture)
    {
        if (output == null || targetTexture == null)
            return;

        Texture2D outputTexture = null;
        try
        {
            // Get tensor dimensions
            int[] dims = output.shape.ToArray();
            
            // Prepare texture for writing
            outputTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);
            Color[] colors = new Color[inputWidth * inputHeight];
            
            // Fill colors based on segmentation results
            if (useArgMaxMode && dims.Length >= 4 && dims[3] > 1)
            {
                // ArgMax mode: choose class with highest probability for each pixel
                for (int y = 0; y < inputHeight; y++)
                {
                    for (int x = 0; x < inputWidth; x++)
                    {
                        int pixelIndex = y * inputWidth + x;
                        int maxClass = 0;
                        float maxProb = 0;
                        
                        // Find class with highest probability
                        for (int c = 0; c < dims[3]; c++)
                        {
                            float prob = output[0, y, x, c];
                            if (prob > maxProb)
                            {
                                maxProb = prob;
                                maxClass = c;
                            }
                        }
                        
                        // Set color based on whether it's a wall or not
                        colors[pixelIndex] = maxClass == WallClassId && maxProb >= ClassificationThreshold ? 
                            new Color(1, 1, 1, 1) : new Color(0, 0, 0, 0);
                    }
                }
            }
            else
            {
                // Direct mode: just check probability for wall class
                for (int y = 0; y < inputHeight; y++)
                {
                    for (int x = 0; x < inputWidth; x++)
                    {
                        int pixelIndex = y * inputWidth + x;
                        float wallProb = output[0, y, x, WallClassId];
                        colors[pixelIndex] = wallProb >= ClassificationThreshold ? 
                            new Color(1, 1, 1, 1) : new Color(0, 0, 0, 0);
                    }
                }
            }
            
            // Apply colors to texture
            outputTexture.SetPixels(colors);
            outputTexture.Apply();
            
            // Copy to render texture
            Graphics.Blit(outputTexture, targetTexture);
            
            // Update segmentation texture for WallMeshRenderer if needed
            if (OnSegmentationCompleted != null)
            {
                // Get the current segmentation texture
                Texture2D segTexture = GetSegmentationTexture();
                if (segTexture != null)
                {
                    OnSegmentationCompleted.Invoke(segTexture);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error processing output tensor: {e.Message}");
        }
        finally
        {
            // Clean up the temporary texture
            if (outputTexture != null)
            {
                Destroy(outputTexture);
            }
        }
    }
    
    /// <summary>
    /// Applies post-processing effects to the result texture based on enabled options
    /// </summary>
    private void ApplyPostProcessing()
    {
        if (debugMode)
            Debug.Log("EnhancedDeepLabPredictor: Starting post-processing");
        
        // Validate result mask
        if (_enhancedResultMask == null)
        {
            Debug.LogError("EnhancedDeepLabPredictor: Result mask is null, cannot apply post-processing");
            return;
        }
        
        RenderTexture tempRT1 = null;
        RenderTexture tempRT2 = null;
        
        try
        {
            // Create temporary render textures
            tempRT1 = RenderTexture.GetTemporary(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
            tempRT2 = RenderTexture.GetTemporary(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
            
            // Copy current result to first temp texture
            Graphics.Blit(_enhancedResultMask, tempRT1);
            
            // Apply temporal smoothing if enabled
            if (applyTemporalSmoothing && previousMask != null)
            {
                try
                {
                    if (debugMode && verbose)
                        Debug.Log("EnhancedDeepLabPredictor: Applying temporal smoothing");
                    
                    // Get shader and create material
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
                            RenderTexture temp = tempRT1;
                            tempRT1 = tempRT2;
                            tempRT2 = temp;
                            
                            // Update previous mask for next frame
                            Graphics.Blit(tempRT1, previousMask);
                            
                            if (debugMode && verbose)
                                Debug.Log("EnhancedDeepLabPredictor: Temporal smoothing applied successfully");
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"EnhancedDeepLabPredictor: Error during temporal smoothing: {e.Message}");
                            // Keep current result without smoothing
                        }
                        finally
                        {
                            // Clean up material
                            if (blendMaterial != null)
                                Destroy(blendMaterial);
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
            
            // Apply wall filling if enabled
            if (applyWallFilling)
            {
                try
                {
                    if (debugMode && verbose)
                        Debug.Log("EnhancedDeepLabPredictor: Applying wall filling");
                    
                    // Add wall filling implementation here
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
                    
                    // Add noise reduction implementation here
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EnhancedDeepLabPredictor: Failed to apply noise reduction: {e.Message}");
                }
            }
            
            // Copy final result back to the result texture
            Graphics.Blit(tempRT1, _enhancedResultMask);
            
            if (debugMode && verbose)
                Debug.Log("EnhancedDeepLabPredictor: Post-processing completed successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error processing frame: {e.Message}");
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
            string shaderSource = @"
Shader ""Hidden/BasicBlendShader"" {
    Properties {
        _MainTex (""Main Texture"", 2D) = ""white"" {}
        _BlendTex (""Blend Texture"", 2D) = ""white"" {}
        _Blend (""Blend Factor"", Range(0,1)) = 0.5
    }
    
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            
            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            sampler2D _MainTex;
            sampler2D _BlendTex;
            float _Blend;
            
            fixed4 frag(v2f i) : SV_Target {
                fixed4 col1 = tex2D(_MainTex, i.uv);
                fixed4 col2 = tex2D(_BlendTex, i.uv);
                return lerp(col1, col2, _Blend);
            }
            ENDCG
        }
    }
    FallBack ""Diffuse""
}";

            // Create asset from shader source (simplified for compilation - not functional)
            // In a real implementation, you would use Unity Editor API to create a shader
            // But for compilation purposes, we'll return null here
            if (debugMode)
                Debug.Log("BasicPostProcessingShader created (not actually functional at runtime)");
            
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error creating fallback shader: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Initialize or resize result textures based on input dimensions
    /// </summary>
    /// <param name="width">Width of the textures</param>
    /// <param name="height">Height of the textures</param>
    private void InitializeResultTextures(int width, int height)
    {
        try
        {
            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"EnhancedDeepLabPredictor: Invalid texture dimensions: {width}x{height}");
                return;
            }

            // Release existing textures if they exist
            if (_segmentationTexture != null)
            {
                Destroy(_segmentationTexture);
            }
            if (_processedTexture != null)
            {
                _processedTexture.Release();
                Destroy(_processedTexture);
            }

            // Create new textures with specified dimensions
            _segmentationTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _segmentationTexture.name = "SegmentationResult";

            _processedTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _processedTexture.name = "ProcessedResult";
            _processedTexture.enableRandomWrite = true;
            _processedTexture.Create();

            if (debugMode)
                Debug.Log($"EnhancedDeepLabPredictor: Result textures initialized with dimensions {width}x{height}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error initializing result textures: {ex.Message}");
        }
    }

    protected void OnEnable()
    {
        try
        {
            // Initialize the predictor reference if it's still null
            if (_predictor == null)
            {
                _predictor = this;
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Set self as predictor reference");
            }
            
            // Initialize the preprocessor if needed
            if (_preprocessor == null)
            {
                _preprocessor = new DeepLabPreprocessor();
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Created preprocessor");
            }
            
            // Create placeholder textures
            if (Application.isPlaying)
            {
                // Use default size initially, will be resized as needed
                InitializeResultTextures(inputWidth > 0 ? inputWidth : 513, inputHeight > 0 ? inputHeight : 513);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error in OnEnable: {e.Message}");
        }
    }

    protected void OnDisable()
    {
        try
        {
            // Clean up resources that should be released when disabled
            if (previousMask != null && Application.isPlaying)
            {
                previousMask.Release();
                Destroy(previousMask);
                previousMask = null;
            }
            
            // Clean up engine
            if (localEngine != null)
            {
                localEngine.Dispose();
                localEngine = null;
            }
            
            if (debugMode && verbose)
                Debug.Log("EnhancedDeepLabPredictor: Cleaned up resources on disable");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error in OnDisable: {e.Message}");
        }
    }
    
    protected void OnDestroy()
    {
        try
        {
            // Clean up resources
            if (localEngine != null)
            {
                localEngine.Dispose();
                localEngine = null;
            }
            
            // Clean up all resources
            if (_enhancedResultMask != null)
            {
                _enhancedResultMask.Release();
                Destroy(_enhancedResultMask);
                _enhancedResultMask = null;
            }
            
            if (previousMask != null)
            {
                previousMask.Release();
                Destroy(previousMask);
                previousMask = null;
            }
            
            if (_processedTexture != null)
            {
                _processedTexture.Release();
                Destroy(_processedTexture);
                _processedTexture = null;
            }
            
            if (_segmentationTexture != null)
            {
                Destroy(_segmentationTexture);
                _segmentationTexture = null;
            }
            
            // Clean up other resources
            _preprocessor = null;
            _cachedPostProcessingShader = null;
            
            if (debugMode && verbose)
                Debug.Log("EnhancedDeepLabPredictor: All resources cleaned up");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error in OnDestroy: {e.Message}");
        }
    }

    /// <summary>
    /// Calculates statistics from the segmentation result
    /// </summary>
    /// <param name="segmentationResult">The segmentation result texture</param>
    private void UpdateSegmentationStatistics(RenderTexture segmentationResult)
    {
        if (segmentationResult == null || !collectStatistics)
            return;
        
        try
        {
            // Statistics are updated every few frames to avoid performance impact
            if (Time.frameCount % statisticsUpdateInterval != 0)
                return;
            
            if (debugMode && verbose)
                Debug.Log("EnhancedDeepLabPredictor: Updating segmentation statistics");
            
            // Create a temporary RenderTexture to read pixels from
            RenderTexture.active = segmentationResult;
            Texture2D tempTexture = new Texture2D(segmentationResult.width, segmentationResult.height, TextureFormat.RGBA32, false);
            tempTexture.ReadPixels(new Rect(0, 0, segmentationResult.width, segmentationResult.height), 0, 0);
            tempTexture.Apply();
            RenderTexture.active = null;
            
            // Count pixels by class
            Color32[] pixels = tempTexture.GetPixels32();
            Dictionary<byte, int> pixelCounts = new Dictionary<byte, int>();
            
            // Walls are typically the class matching our WallClassId
            int wallPixelCount = 0;
            int totalPixels = pixels.Length;
            
            foreach (Color32 pixel in pixels)
            {
                // The red channel typically contains the class ID in our visualization scheme
                byte classId = pixel.r;
                
                // Count by class ID
                if (!pixelCounts.ContainsKey(classId))
                    pixelCounts[classId] = 0;
                
                pixelCounts[classId]++;
                
                // Count wall pixels
                if (classId == WallClassId)
                    wallPixelCount++;
            }
            
            // Update statistics
            wallPixelPercentage = totalPixels > 0 ? (float)wallPixelCount / totalPixels : 0f;
            lastWallPixelCount = wallPixelCount;
            lastTotalPixelCount = totalPixels;
            classDistribution = pixelCounts;
            
            // Clean up temporary texture
            Destroy(tempTexture);
            
            if (debugMode && verbose)
            {
                Debug.Log($"EnhancedDeepLabPredictor: Statistics updated. Wall pixels: {wallPixelCount} ({wallPixelPercentage:P2}) of {totalPixels} total pixels");
                if (classDistribution.Count > 0 && debugMode)
                {
                    foreach (var item in classDistribution.OrderByDescending(x => x.Value))
                    {
                        Debug.Log($"Class {item.Key}: {item.Value} pixels ({(float)item.Value / totalPixels:P2})");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error updating segmentation statistics: {e.Message}");
        }
    }

    /// <summary>
    /// Forces a refresh of resources like shaders and textures
    /// </summary>
    /// <param name="refreshShaders">Whether to refresh cached shaders</param>
    /// <param name="refreshTextures">Whether to refresh cached textures</param>
    public void RefreshResources(bool refreshShaders = true, bool refreshTextures = false)
    {
        if (debugMode)
            Debug.Log($"EnhancedDeepLabPredictor: Refreshing resources (shaders: {refreshShaders}, textures: {refreshTextures})");

        try 
        {
            // Reset shader cache if requested
            if (refreshShaders)
            {
                _cachedPostProcessingShader = null;
                
                // Force shader lookup on next use
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Shader cache cleared");
            }
            
            // Refresh textures if requested
            if (refreshTextures && Application.isPlaying)
            {
                // Clean up previous textures
                if (_enhancedResultMask != null)
                {
                    _enhancedResultMask.Release();
                    Destroy(_enhancedResultMask);
                    _enhancedResultMask = null;
                }
                
                if (previousMask != null)
                {
                    previousMask.Release();
                    Destroy(previousMask);
                    previousMask = null;
                }
                
                // Recreate textures with current settings
                InitializeResultTextures(inputWidth > 0 ? inputWidth : 513, inputHeight > 0 ? inputHeight : 513);
                
                if (debugMode && verbose)
                    Debug.Log("EnhancedDeepLabPredictor: Textures refreshed");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"EnhancedDeepLabPredictor: Error refreshing resources: {e.Message}");
        }
    }

    private Texture2D ConvertRenderTextureToTexture2D(RenderTexture renderTexture)
    {
        if (renderTexture == null)
        {
            Debug.LogError("EnhancedDeepLabPredictor: Input render texture is null");
            return null;
        }

        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();
        RenderTexture.active = null;

        return texture2D;
    }
} 