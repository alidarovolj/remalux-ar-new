using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using Unity.XR.CoreUtils;

/// <summary>
/// Manages AR Foundation components and provides centralized access to them.
/// Only handles AR setup and tracking, not visual representation or business logic.
/// </summary>
[RequireComponent(typeof(ARSession))]
[RequireComponent(typeof(XROrigin))]
[RequireComponent(typeof(ARPlaneManager))]
[RequireComponent(typeof(ARRaycastManager))]
public class ARSystemManager : MonoBehaviour
{
    [Header("AR Components")]
    public ARCameraManager CameraManager { get; private set; }
    public ARPlaneManager PlaneManager { get; private set; }
    public ARRaycastManager RaycastManager { get; private set; }
    public ARAnchorManager AnchorManager { get; private set; }
    public AROcclusionManager OcclusionManager { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool enableOcclusion = true;
    [SerializeField] private PlaneDetectionMode detectionMode = PlaneDetectionMode.Vertical;
    [SerializeField] private bool autoFocusRequested = true;
    
    public Camera ARCamera { get; private set; }
    
    // Event raised when AR system is fully initialized
    public event System.Action OnARInitialized;

    private void Awake()
    {
        // Get required components
        var xrOrigin = GetComponent<XROrigin>();
        CameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
        if (CameraManager == null)
            CameraManager = xrOrigin.Camera.gameObject.AddComponent<ARCameraManager>();
            
        PlaneManager = GetComponent<ARPlaneManager>();
        RaycastManager = GetComponent<ARRaycastManager>();
        
        // Get or add AnchorManager
        AnchorManager = GetComponent<ARAnchorManager>();
        if (AnchorManager == null)
            AnchorManager = gameObject.AddComponent<ARAnchorManager>();
        
        // Set AR camera reference
        ARCamera = xrOrigin.Camera;
        
        // Configure plane detection
        PlaneManager.requestedDetectionMode = detectionMode;
        
        // Setup occlusion if supported and enabled
        if (enableOcclusion)
        {
            OcclusionManager = GetComponent<AROcclusionManager>();
            if (OcclusionManager == null)
                OcclusionManager = gameObject.AddComponent<AROcclusionManager>();
                
            // Enable depth and stencil
            OcclusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            OcclusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
        }
        
        // Configure camera
        if (CameraManager != null)
        {
            #if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
            // Use focus mode on actual devices
            CameraManager.focusMode = autoFocusRequested ? 
                CameraFocusMode.Auto : CameraFocusMode.Fixed;
            #endif
        }
    }

    private void Start()
    {
        OnARInitialized?.Invoke();
    }

    /// <summary>
    /// Attempts to create an anchor at the specified position on a detected plane
    /// </summary>
    public ARAnchor CreateAnchor(Vector3 position, Quaternion rotation)
    {
        if (AnchorManager == null) 
            return null;
        
        // Try to find a plane at this position
        var hits = new List<ARRaycastHit>();
        RaycastManager.Raycast(new Ray(position + Vector3.up * 0.1f, Vector3.down), hits, TrackableType.PlaneWithinPolygon);
        
        if (hits.Count > 0)
        {
            var trackableId = hits[0].trackableId;
            var plane = PlaneManager.GetPlane(trackableId);
            
            if (plane != null)
            {
                // Create an anchor attached to this plane
                GameObject anchorObj = new GameObject("Plane Anchor");
                anchorObj.transform.position = position;
                anchorObj.transform.rotation = rotation;
                anchorObj.transform.parent = plane.transform;
                
                return anchorObj.AddComponent<ARAnchor>();
            }
        }
        
        // If no plane found, create a free-floating anchor
        GameObject freeAnchorObj = new GameObject("Free Anchor");
        freeAnchorObj.transform.position = position;
        freeAnchorObj.transform.rotation = rotation;
        
        return freeAnchorObj.AddComponent<ARAnchor>();
    }

    /// <summary>
    /// Sets the plane detection mode at runtime
    /// </summary>
    public void SetPlaneDetectionMode(PlaneDetectionMode mode)
    {
        detectionMode = mode;
        if (PlaneManager != null)
            PlaneManager.requestedDetectionMode = mode;
    }
    
    /// <summary>
    /// Check if AR session is ready
    /// </summary>
    public bool IsSessionReady()
    {
        // Basic check - can be expanded with more specific conditions
        return ARCamera != null && PlaneManager != null;
    }
    
    /// <summary>
    /// Toggle plane detection on/off
    /// </summary>
    public void TogglePlaneDetection(bool enabled)
    {
        if (PlaneManager != null)
        {
            PlaneManager.enabled = enabled;
            
            // Also hide existing planes if disabled
            foreach (var plane in PlaneManager.trackables)
            {
                plane.gameObject.SetActive(enabled);
            }
            
            Debug.Log($"Plane detection {(enabled ? "enabled" : "disabled")}");
        }
    }
} 