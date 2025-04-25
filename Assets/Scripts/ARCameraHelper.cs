using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;

/// <summary>
/// Helper component to fix and check AR camera issues at runtime
/// </summary>
public class ARCameraHelper : MonoBehaviour
{
    [SerializeField] private bool fixOnStart = true;
    [SerializeField] private bool autoDestroySelfAfterFix = true;
    
    private XROrigin _xrOrigin;
    private Camera _arCamera;
    private ARWallSystem _arWallSystem;
    
    private void Start()
    {
        if (fixOnStart)
        {
            StartCoroutine(FixARCameraDelayed());
        }
    }
    
    private IEnumerator FixARCameraDelayed()
    {
        // Wait for one frame to let ARBootstrapper initialize first
        yield return null;
        
        // Run the fix
        FixARCamera();
        
        if (autoDestroySelfAfterFix)
        {
            Destroy(this);
        }
    }
    
    /// <summary>
    /// Find and fix AR camera issues at runtime
    /// </summary>
    public void FixARCamera()
    {
        // Find required components
        _xrOrigin = FindObjectOfType<XROrigin>();
        _arWallSystem = FindObjectOfType<ARWallSystem>();
        
        if (_xrOrigin == null)
        {
            Debug.LogError("ARCameraHelper: No XR Origin found in the scene!");
            return;
        }
        
        // Get the camera offset
        GameObject cameraOffset = _xrOrigin.CameraFloorOffsetObject;
        if (cameraOffset == null)
        {
            cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(_xrOrigin.transform, false);
            _xrOrigin.CameraFloorOffsetObject = cameraOffset;
            Debug.Log("ARCameraHelper: Created missing Camera Offset");
        }
        
        // Check if camera exists
        _arCamera = _xrOrigin.Camera;
        GameObject arCameraObj = null;
        
        if (_arCamera == null)
        {
            // Try to find any camera under camera offset
            foreach (Transform child in cameraOffset.transform)
            {
                Camera childCamera = child.GetComponent<Camera>();
                if (childCamera != null)
                {
                    _arCamera = childCamera;
                    arCameraObj = childCamera.gameObject;
                    _xrOrigin.Camera = _arCamera;
                    Debug.Log("ARCameraHelper: Found camera under Camera Offset and assigned to XR Origin");
                    break;
                }
            }
            
            // If still no camera, create one
            if (_arCamera == null)
            {
                arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffset.transform, false);
                _arCamera = arCameraObj.AddComponent<Camera>();
                _arCamera.clearFlags = CameraClearFlags.SolidColor;
                _arCamera.backgroundColor = Color.black;
                _arCamera.nearClipPlane = 0.1f;
                _arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                _xrOrigin.Camera = _arCamera;
                Debug.Log("ARCameraHelper: Created new AR Camera and assigned to XR Origin");
            }
        }
        else
        {
            arCameraObj = _arCamera.gameObject;
            
            // Ensure camera is under Camera Offset
            if (arCameraObj.transform.parent != cameraOffset.transform)
            {
                arCameraObj.transform.SetParent(cameraOffset.transform, true);
                Debug.Log("ARCameraHelper: Moved AR Camera under Camera Offset");
            }
        }
        
        // Add required AR components
        ARCameraManager cameraManager = arCameraObj.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            cameraManager = arCameraObj.AddComponent<ARCameraManager>();
            Debug.Log("ARCameraHelper: Added AR Camera Manager");
        }
        
        ARCameraBackground cameraBackground = arCameraObj.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
            Debug.Log("ARCameraHelper: Added AR Camera Background");
        }
        
        // Check for Tracked Pose Driver
        bool hasTrackedPoseDriver = false;
        var components = arCameraObj.GetComponents<MonoBehaviour>();
        
        foreach (var component in components)
        {
            string typeName = component.GetType().Name;
            if (typeName == "TrackedPoseDriver" || typeName == "ARTrackedPoseDriver" || 
                typeName.Contains("TrackedPose"))
            {
                hasTrackedPoseDriver = true;
                Debug.Log("ARCameraHelper: Found Tracked Pose Driver: " + typeName);
                break;
            }
        }
        
        if (!hasTrackedPoseDriver)
        {
            Debug.LogWarning("ARCameraHelper: No Tracked Pose Driver found on AR Camera. Adding one if possible...");
            
            // Try to add via reflection
            try
            {
                // Try Input System TPD first
                System.Type tpdType = System.Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
                if (tpdType != null)
                {
                    var driver = arCameraObj.AddComponent(tpdType);
                    Debug.Log("ARCameraHelper: Added Input System Tracked Pose Driver");
                    hasTrackedPoseDriver = true;
                }
                else
                {
                    // Try legacy TPD
                    System.Type legacyType = System.Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
                    if (legacyType != null)
                    {
                        var driver = arCameraObj.AddComponent(legacyType);
                        Debug.Log("ARCameraHelper: Added Legacy Tracked Pose Driver");
                        hasTrackedPoseDriver = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("ARCameraHelper: Failed to add Tracked Pose Driver: " + ex.Message);
            }
            
            if (!hasTrackedPoseDriver)
            {
                Debug.LogError("ARCameraHelper: Could not add Tracked Pose Driver! You must add it manually in the Inspector.");
            }
        }
        
        // Update ARWallSystem if found
        if (_arWallSystem != null)
        {
            _arWallSystem.CameraManager = cameraManager;
            _arWallSystem.UpdateCameraReference();
            Debug.Log("ARCameraHelper: Updated ARWallSystem with new camera references");
        }
        
        Debug.Log("ARCameraHelper: AR Camera fix completed");
    }
} 