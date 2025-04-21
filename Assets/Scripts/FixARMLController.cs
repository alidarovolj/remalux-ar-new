using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;

/// <summary>
/// Fixes common issues with ARMLController by providing proper initialization
/// and improving session state handling in AR Wall Detection system.
/// </summary>
public class FixARMLController : MonoBehaviour
{
    [Header("Component References")]
    [Tooltip("Reference to the AR Session")]
    public ARSession arSession;
    
    [Tooltip("Reference to the ARML Controller to fix")]
    public ARMLController armlController;
    
    [Header("Fix Settings")]
    [Tooltip("Automatically find and apply fixes on startup")]
    public bool autoFixOnStart = true;
    
    [Tooltip("Fix initialization delays and timing issues")]
    public bool fixInitialization = true;
    
    [Tooltip("Fix event handling and connections")]
    public bool fixEventHandling = true;
    
    [Tooltip("Fix session tracking issues")]
    public bool fixTracking = true;
    
    [Tooltip("Log details about the fixes being applied")]
    public bool logFixDetails = true;
    
    // Internal variables for tracking state
    private ARSessionStateChangedEventArgs lastSessionState;
    
    private void Start()
    {
        // Find references if not assigned
        FindReferences();
        
        if (autoFixOnStart)
        {
            // Apply fixes after a short delay to ensure all components are initialized
            StartCoroutine(ApplyFixesDelayed());
        }
    }
    
    private void FindReferences()
    {
        if (armlController == null)
        {
            armlController = GetComponent<ARMLController>();
            
            if (armlController == null)
            {
                armlController = FindFirstObjectByType<ARMLController>();
                
                if (armlController == null)
                {
                    Debug.LogWarning("FixARMLController: Could not find ARMLController to fix");
                }
                else if (logFixDetails)
                {
                    Debug.Log("FixARMLController: Found ARMLController in scene");
                }
            }
        }
        
        if (arSession == null)
        {
            arSession = FindFirstObjectByType<ARSession>();
            
            if (arSession == null)
            {
                Debug.LogWarning("FixARMLController: Could not find ARSession");
            }
            else if (logFixDetails)
            {
                Debug.Log("FixARMLController: Found ARSession in scene");
            }
        }
    }
    
    private IEnumerator ApplyFixesDelayed()
    {
        // Wait for components to initialize
        yield return new WaitForSeconds(0.5f);
        
        ApplyFixes();
        
        // Subscribe to session state changes for continuous fixing
        if (fixTracking)
        {
            SubscribeToSessionEvents();
        }
    }
    
    public void ApplyFixes()
    {
        if (armlController == null)
        {
            Debug.LogError("FixARMLController: No ARMLController reference to fix");
            return;
        }
        
        if (logFixDetails)
        {
            Debug.Log("FixARMLController: Applying fixes to ARMLController");
        }
        
        // Fix 1: Ensure AR session is properly referenced and initialized
        if (fixInitialization)
        {
            FixInitialization();
        }
        
        // Fix 2: Ensure event handlers are properly connected
        if (fixEventHandling)
        {
            FixEventHandling();
        }
        
        // Fix 3: Fix tracking issues if necessary
        if (fixTracking)
        {
            FixTracking();
        }
        
        if (logFixDetails)
        {
            Debug.Log("FixARMLController: All fixes applied successfully");
        }
    }
    
    private void FixInitialization()
    {
        // Check for an ARManager component
        ARManager arManager = FindFirstObjectByType<ARManager>();
        
        if (arManager != null)
        {
            // Use reflection to ensure proper initialization
            var sessionField = armlController.GetType().GetField("arManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (sessionField != null)
            {
                var currentManager = sessionField.GetValue(armlController);
                
                if (currentManager == null)
                {
                    // Set the ARManager field if it's null
                    sessionField.SetValue(armlController, arManager);
                    if (logFixDetails)
                        Debug.Log("FixARMLController: Fixed missing ARManager reference");
                }
            }
        }
        
        // Fix initialization timing issues
        StartCoroutine(EnsureProperStartup());
    }
    
    private IEnumerator EnsureProperStartup()
    {
        float waitTime = 0;
        float maxWaitTime = 5f; // Maximum time to wait for session
        
        while (ARSession.state != ARSessionState.Ready && 
               ARSession.state != ARSessionState.SessionTracking && 
               waitTime < maxWaitTime)
        {
            waitTime += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        
        // If we've waited and the session is now ready, invoke StartAR on the controller
        if (ARSession.state == ARSessionState.Ready || ARSession.state == ARSessionState.SessionTracking)
        {
            // Small additional delay to ensure everything is initialized
            yield return new WaitForSeconds(0.5f);
            
            // Call StartAR method on ARMLController if it's not already started
            var method = armlController.GetType().GetMethod("StartAR");
            if (method != null)
            {
                // Check if AR is already started by looking at a private field
                var arStartedField = armlController.GetType().GetField("arStarted", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (arStartedField != null)
                {
                    bool isStarted = (bool)arStartedField.GetValue(armlController);
                    
                    if (!isStarted)
                    {
                        if (logFixDetails)
                            Debug.Log("FixARMLController: Invoking StartAR since session is ready but AR not started");
                        
                        method.Invoke(armlController, null);
                    }
                }
            }
        }
        else if (waitTime >= maxWaitTime)
        {
            Debug.LogWarning("FixARMLController: Timeout waiting for AR session to be ready");
        }
    }
    
    private void FixEventHandling()
    {
        // Check for deep learning predictor
        DeepLabPredictor predictor = FindFirstObjectByType<DeepLabPredictor>();
        
        if (predictor != null)
        {
            // If enhanced predictor exists, use that instead (now located in ML/DeepLab)
            EnhancedDeepLabPredictor enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
            
            if (enhancedPredictor != null)
            {
                // Connect events - we'll handle forwarding instead of direct field assignment
                // since EnhancedDeepLabPredictor is not compatible with DeepLabPredictor
                enhancedPredictor.OnSegmentationResult += OnSegmentationResult;
                
                if (logFixDetails)
                    Debug.Log("FixARMLController: Connected to EnhancedDeepLabPredictor events");
            }
            else
            {
                // If enhanced predictor doesn't exist, try to create one
                GameObject enhancedPredictorObj = new GameObject("Enhanced DeepLab Predictor");
                enhancedPredictor = enhancedPredictorObj.AddComponent<EnhancedDeepLabPredictor>();
                
                if (enhancedPredictor != null)
                {
                    // Connect events
                    enhancedPredictor.OnSegmentationResult += OnSegmentationResult;
                    
                    if (logFixDetails)
                        Debug.Log("FixARMLController: Created and connected to new EnhancedDeepLabPredictor");
                }
            }
        }
        
        // Fix wall colorizer connection if needed
        WallColorizer colorizer = FindFirstObjectByType<WallColorizer>();
        
        if (colorizer != null)
        {
            // Use reflection to ensure wall colorizer is properly connected
            var colorizerField = armlController.GetType().GetField("wallColorizer", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (colorizerField != null)
            {
                var currentColorizer = colorizerField.GetValue(armlController);
                
                if (currentColorizer == null)
                {
                    // Set the wall colorizer field if it's null
                    colorizerField.SetValue(armlController, colorizer);
                    
                    if (logFixDetails)
                        Debug.Log("FixARMLController: Fixed missing WallColorizer reference");
                }
            }
        }
    }
    
    private void FixTracking()
    {
        // Handle tracking issues based on session state
        switch (ARSession.state)
        {
            case ARSessionState.None:
            case ARSessionState.Unsupported:
                Debug.LogWarning("FixARMLController: AR is not supported on this device");
                break;
                
            case ARSessionState.NeedsInstall:
                Debug.Log("FixARMLController: AR requires installation, attempting to install...");
                StartCoroutine(InstallARAndRetry());
                break;
                
            case ARSessionState.Installing:
                Debug.Log("FixARMLController: AR is installing, will retry when complete");
                break;
                
            case ARSessionState.Ready:
            case ARSessionState.SessionInitializing:
            case ARSessionState.SessionTracking:
                // Already in a good state
                break;
        }
    }
    
    private IEnumerator InstallARAndRetry()
    {
        // Wait for installation to complete
        while (ARSession.state == ARSessionState.Installing || ARSession.state == ARSessionState.NeedsInstall)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        if (ARSession.state == ARSessionState.Ready || ARSession.state == ARSessionState.SessionTracking)
        {
            // Retry applying fixes after installation
            ApplyFixes();
        }
    }
    
    private void SubscribeToSessionEvents()
    {
        ARSession.stateChanged += OnARSessionStateChanged;
        
        if (logFixDetails)
            Debug.Log("FixARMLController: Subscribed to AR session state changes");
    }
    
    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        lastSessionState = args;
        
        // Handle state changes
        if (args.state == ARSessionState.SessionTracking)
        {
            // If we transitioned to tracking, make sure AR is started
            if (armlController != null && !IsARStarted())
            {
                armlController.StartAR();
                
                if (logFixDetails)
                    Debug.Log("FixARMLController: AR session is tracking, ensured AR is started");
            }
        }
        else if (args.state == ARSessionState.SessionInitializing)
        {
            // Handle initialization
            if (logFixDetails)
                Debug.Log("FixARMLController: AR session is initializing");
        }
    }
    
    private bool IsARStarted()
    {
        // Check if AR is started using reflection
        var arStartedField = armlController.GetType().GetField("arStarted", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (arStartedField != null)
        {
            return (bool)arStartedField.GetValue(armlController);
        }
        
        return false; // Default if we can't determine
    }
    
    // Handler for segmentation results to ensure they're passed to ARMLController
    private void OnSegmentationResult(RenderTexture segmentationMask)
    {
        if (armlController != null)
        {
            // Use reflection to call the private HandleSegmentationResult method
            var method = armlController.GetType().GetMethod("HandleSegmentationResult", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (method != null)
            {
                method.Invoke(armlController, new object[] { segmentationMask });
                
                if (logFixDetails && Time.frameCount % 100 == 0) // Log only occasionally to avoid spam
                    Debug.Log("FixARMLController: Forwarded segmentation result to ARMLController");
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        ARSession.stateChanged -= OnARSessionStateChanged;
        
        // Disconnect from any other events we subscribed to
        EnhancedDeepLabPredictor enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
        if (enhancedPredictor != null)
        {
            enhancedPredictor.OnSegmentationResult -= OnSegmentationResult;
        }
    }
} 