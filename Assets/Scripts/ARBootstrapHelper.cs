using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;

/// <summary>
/// Helper class to bootstrap AR functionality at runtime
/// Handles initialization, session management, and common AR-related tasks
/// </summary>
public class ARBootstrapHelper : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARSession arSession;
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARMeshManager meshManager;
    
    [Header("Settings")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool initialPlaneDetection = true;
    [SerializeField] private bool initialMeshDetection = true;
    [SerializeField] private float initializationTimeout = 10f;
    
    [Header("Debug")]
    [SerializeField] private bool debugMode = false;
    
    private bool isInitialized = false;
    private bool isStarted = false;
    
    void Start()
    {
        if (autoStart)
        {
            StartCoroutine(InitializeAR());
        }
    }
    
    /// <summary>
    /// Initializes AR components and starts AR session
    /// </summary>
    public void StartAR()
    {
        if (isStarted)
            return;
            
        StartCoroutine(InitializeAR());
    }
    
    /// <summary>
    /// Stops AR session
    /// </summary>
    public void StopAR()
    {
        if (!isStarted)
            return;
            
        if (arSession != null)
        {
            arSession.enabled = false;
        }
        
        if (planeManager != null)
        {
            planeManager.enabled = false;
        }
        
        if (meshManager != null)
        {
            meshManager.enabled = false;
        }
        
        isStarted = false;
        
        if (debugMode)
            Debug.Log("ARBootstrapHelper: AR session stopped");
    }
    
    /// <summary>
    /// Toggles plane detection on/off
    /// </summary>
    public void TogglePlaneDetection(bool enable)
    {
        if (planeManager != null)
        {
            planeManager.enabled = enable;
            
            if (debugMode)
                Debug.Log($"ARBootstrapHelper: Plane detection {(enable ? "enabled" : "disabled")}");
        }
    }
    
    /// <summary>
    /// Toggles mesh detection on/off
    /// </summary>
    public void ToggleMeshDetection(bool enable)
    {
        if (meshManager != null)
        {
            meshManager.enabled = enable;
            
            if (debugMode)
                Debug.Log($"ARBootstrapHelper: Mesh detection {(enable ? "enabled" : "disabled")}");
        }
    }
    
    /// <summary>
    /// Initializes AR components and waits for session to be ready
    /// </summary>
    private IEnumerator InitializeAR()
    {
        if (!isInitialized)
        {
            // Find missing components if needed
            FindARComponents();
            
            // Check for critical components
            if (arSession == null || xrOrigin == null)
            {
                Debug.LogError("ARBootstrapHelper: Required AR components (ARSession or XROrigin) are missing!");
                yield break;
            }
            
            // Wait for AR availability check
            yield return ARSession.CheckAvailability();
            
            // Check if device supports AR
            if (ARSession.state == ARSessionState.Unsupported)
            {
                Debug.LogError("ARBootstrapHelper: AR is not supported on this device");
                yield break;
            }
            
            isInitialized = true;
        }
        
        // Enable session
        if (arSession != null && !arSession.enabled)
        {
            arSession.enabled = true;
        }
        
        // Wait for session initialization
        float timeout = Time.time + initializationTimeout;
        while (ARSession.state != ARSessionState.Ready && 
               ARSession.state != ARSessionState.SessionInitializing && 
               ARSession.state != ARSessionState.SessionTracking)
        {
            if (Time.time > timeout)
            {
                Debug.LogError("ARBootstrapHelper: AR session initialization timed out");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
        }
        
        // Configure components
        if (planeManager != null)
        {
            planeManager.enabled = initialPlaneDetection;
        }
        
        if (meshManager != null)
        {
            meshManager.enabled = initialMeshDetection;
        }
        
        isStarted = true;
        
        if (debugMode)
            Debug.Log("ARBootstrapHelper: AR session started successfully");
    }
    
    /// <summary>
    /// Attempts to find missing AR components in the scene
    /// </summary>
    private void FindARComponents()
    {
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
            if (arSession != null && debugMode)
                Debug.Log("ARBootstrapHelper: Found ARSession in scene");
        }
        
        if (xrOrigin == null)
        {
            xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin != null && debugMode)
                Debug.Log("ARBootstrapHelper: Found XROrigin in scene");
        }
        
        if (cameraManager == null && xrOrigin != null)
        {
            cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
            if (cameraManager != null && debugMode)
                Debug.Log("ARBootstrapHelper: Found ARCameraManager in scene");
        }
        
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager != null && debugMode)
                Debug.Log("ARBootstrapHelper: Found ARPlaneManager in scene");
        }
        
        if (meshManager == null)
        {
            meshManager = FindObjectOfType<ARMeshManager>();
            if (meshManager != null && debugMode)
                Debug.Log("ARBootstrapHelper: Found ARMeshManager in scene");
        }
    }
    
    /// <summary>
    /// Returns current status of AR session
    /// </summary>
    public bool IsSessionReady()
    {
        return isInitialized && isStarted && 
               (ARSession.state == ARSessionState.Ready || 
                ARSession.state == ARSessionState.SessionTracking);
    }
} 