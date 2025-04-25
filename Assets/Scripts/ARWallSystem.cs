using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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
        if (CameraManager != null)
        {
            ARCamera = CameraManager.GetComponent<Camera>();
        }
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
        
        // Notify listeners that initialization is complete
        OnARInitialized?.Invoke();
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