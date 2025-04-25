using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

/// <summary>
/// Manages AR components for wall painting application.
/// Provides access to camera and anchoring functionality.
/// </summary>
[RequireComponent(typeof(ARCameraManager), typeof(ARAnchorManager), typeof(ARPlaneManager))]
public class ARWallSystem : MonoBehaviour
{
    [Header("AR Components")]
    public ARCameraManager CameraManager;
    public ARAnchorManager AnchorManager;
    public ARPlaneManager PlaneManager;
    
    [Header("Settings")]
    [SerializeField] private bool autoFocusRequested = false;
    
    public Camera ARCamera { get; private set; }

    // Event fired when AR system is initialized
    public event System.Action OnARInitialized;

    private void Awake()
    {
        // Cache component references if they aren't set
        if (CameraManager == null) CameraManager = GetComponent<ARCameraManager>();
        if (AnchorManager == null) AnchorManager = GetComponent<ARAnchorManager>();
        if (PlaneManager == null) PlaneManager = GetComponent<ARPlaneManager>();
        
        // Cache the camera reference
        UpdateCameraReference();
    }

    private void Start()
    {
        // Configure camera focus mode if available
        if (CameraManager != null)
        {
            #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
            // Use focus mode on actual devices
            CameraManager.focusMode = autoFocusRequested ? 
                CameraFocusMode.Auto : CameraFocusMode.Fixed;
            #endif
        }
        
        // Ensure camera is set up before initialization
        if (ARCamera == null)
        {
            // Try to find the camera in the scene
            UpdateCameraReference();
            
            // If we still don't have a camera, warn about it
            if (ARCamera == null)
            {
                Debug.LogWarning("ARWallSystem: No AR Camera found. Make sure an AR Camera exists in the scene.");
            }
        }
        
        // Notify listeners that initialization is complete
        OnARInitialized?.Invoke();
    }
    
    public void UpdateCameraReference()
    {
        // First try to get camera from CameraManager component
        if (CameraManager != null)
        {
            ARCamera = CameraManager.GetComponent<Camera>();
            if (ARCamera != null) return;
        }
        
        // If that fails, try to find camera in XR Origin structure
        XROrigin xrOrigin = GetComponentInParent<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            ARCamera = xrOrigin.Camera;
            // Also update the CameraManager reference
            if (CameraManager == null)
            {
                CameraManager = ARCamera.GetComponent<ARCameraManager>();
            }
            return;
        }
        
        // If all else fails, try to find any camera tagged as MainCamera
        ARCamera = Camera.main;
        if (ARCamera != null && CameraManager == null)
        {
            CameraManager = ARCamera.GetComponent<ARCameraManager>();
        }
    }

    /// <summary>
    /// Creates a free-floating anchor at the specified position and rotation
    /// </summary>
    public ARAnchor AddAnchor(Vector3 position, Quaternion rotation)
    {
        if (AnchorManager == null)
            return null;
            
        // Create a GameObject and add an ARAnchor component
        GameObject anchorObject = new GameObject("AR Anchor");
        anchorObject.transform.position = position;
        anchorObject.transform.rotation = rotation;
        
        // Add the ARAnchor component
        ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();
        
        return anchor;
    }
} 