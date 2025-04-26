using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Utility class for configuring and ensuring proper AR Camera setup
/// </summary>
public static class ARCameraSetup
{
    /// <summary>
    /// Ensures that an AR Camera exists and is properly configured in the scene
    /// </summary>
    public static void EnsureARCameraExists()
    {
        Debug.Log("ARCameraSetup: Checking AR Camera configuration");

        // First, find XROrigin in the scene
        var xrOrigins = Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        if (xrOrigins == null || xrOrigins.Length == 0)
        {
            Debug.LogWarning("ARCameraSetup: No XROrigin found in the scene. Unable to set up AR Camera.");
            return;
        }

        foreach (var origin in xrOrigins)
        {
            if (origin.Camera == null)
            {
                Debug.LogWarning("ARCameraSetup: XROrigin has no camera assigned. Looking for a suitable camera...");
                
                // Try to find the main camera or any camera under Camera Offset
                Camera cameraToUse = FindCameraForXROrigin(origin);
                
                if (cameraToUse != null)
                {
                    origin.Camera = cameraToUse;
                    Debug.Log($"ARCameraSetup: Assigned camera '{cameraToUse.name}' to XROrigin");
                    
                    // Ensure the camera has ARCameraManager
                    EnsureARCameraManager(cameraToUse.gameObject);
                }
                else
                {
                    Debug.LogWarning("ARCameraSetup: No suitable camera found for XROrigin");
                }
            }
            else
            {
                // Camera exists, ensure it has ARCameraManager
                EnsureARCameraManager(origin.Camera.gameObject);
            }
        }
    }
    
    /// <summary>
    /// Find a suitable camera to use with XROrigin
    /// </summary>
    private static Camera FindCameraForXROrigin(XROrigin origin)
    {
        // First, check for Camera.main
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Debug.Log("ARCameraSetup: Found main camera to use with XROrigin");
            return mainCamera;
        }
        
        // Next, look for a camera under Camera Offset if it exists
        Transform cameraOffset = null;
        
        // Find the Camera Offset
        foreach (Transform child in origin.transform)
        {
            if (child.name.Contains("Camera Offset") || child.name.Contains("CameraOffset"))
            {
                cameraOffset = child;
                break;
            }
        }
        
        // If we found a camera offset, look for cameras underneath
        if (cameraOffset != null)
        {
            Camera[] cameras = cameraOffset.GetComponentsInChildren<Camera>();
            if (cameras.Length > 0)
            {
                Debug.Log($"ARCameraSetup: Found camera '{cameras[0].name}' under Camera Offset");
                return cameras[0];
            }
        }
        
        // As a last resort, look for any camera in the scene
        Camera[] allCameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        if (allCameras.Length > 0)
        {
            Debug.Log($"ARCameraSetup: Using camera '{allCameras[0].name}' found in the scene");
            return allCameras[0];
        }
        
        return null;
    }
    
    /// <summary>
    /// Ensures that the AR camera has an ARCameraManager component
    /// </summary>
    private static void EnsureARCameraManager(GameObject cameraObject)
    {
        if (cameraObject == null)
        {
            Debug.LogWarning("ARCameraSetup: Camera object is null, cannot add ARCameraManager");
            return;
        }
        
        // Check if ARCameraManager already exists
        ARCameraManager cameraManager = cameraObject.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            Debug.Log($"ARCameraSetup: Adding ARCameraManager to {cameraObject.name}");
            cameraManager = cameraObject.AddComponent<ARCameraManager>();
        }
        
        // Check if ARCameraBackground exists
        ARCameraBackground cameraBackground = cameraObject.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            Debug.Log($"ARCameraSetup: Adding ARCameraBackground to {cameraObject.name}");
            cameraBackground = cameraObject.AddComponent<ARCameraBackground>();
        }
        
#if UNITY_IOS || UNITY_ANDROID
        // Configure auto-focus for mobile devices
        if (cameraManager != null)
        {
            Debug.Log("ARCameraSetup: Setting up camera focus mode for mobile");
            cameraManager.autoFocusRequested = true;
        }
#endif
    }
}

/// <summary>
/// Utility script to fix AR Camera setup issues
/// </summary>
public class ARCameraSetupTool : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("AR/Fix AR Camera Setup")]
    public static void FixARCameraSetup()
    {
        // Find XR Origin in the scene
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("No XR Origin found in the scene. Please set up AR Foundation first.");
            return;
        }

        // Find Camera Offset
        GameObject cameraOffset = xrOrigin.CameraFloorOffsetObject;
        if (cameraOffset == null)
        {
            cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform, false);
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            Debug.Log("Created Camera Offset");
        }

        // Check if AR Camera already exists
        GameObject arCameraObj = null;
        Camera arCamera = xrOrigin.Camera;
        
        if (arCamera != null)
        {
            arCameraObj = arCamera.gameObject;
            Debug.Log("Found existing AR Camera: " + arCameraObj.name);
        }
        else
        {
            // Look for AR Camera under Camera Offset
            foreach (Transform child in cameraOffset.transform)
            {
                Camera childCamera = child.GetComponent<Camera>();
                if (childCamera != null)
                {
                    arCameraObj = child.gameObject;
                    arCamera = childCamera;
                    Debug.Log("Found camera under Camera Offset: " + arCameraObj.name);
                    break;
                }
            }
            
            // If still not found, create a new AR Camera
            if (arCameraObj == null)
            {
                arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffset.transform, false);
                arCamera = arCameraObj.AddComponent<Camera>();
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                Debug.Log("Created new AR Camera");
            }
            
            // Set camera in XR Origin
            xrOrigin.Camera = arCamera;
        }
        
        // Add required AR components
        ARCameraManager cameraManager = arCameraObj.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            cameraManager = arCameraObj.AddComponent<ARCameraManager>();
            Debug.Log("Added AR Camera Manager");
        }
        
        ARCameraBackground cameraBackground = arCameraObj.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
            Debug.Log("Added AR Camera Background");
        }
        
        // Check for Tracked Pose Driver
        bool hasTrackedPoseDriver = false;
        
        // Check for Input System Tracked Pose Driver
        var tpdComponents = arCameraObj.GetComponents<MonoBehaviour>();
        foreach (var component in tpdComponents)
        {
            string typeName = component.GetType().Name;
            if (typeName == "TrackedPoseDriver" || typeName == "ARTrackedPoseDriver" || 
                typeName.Contains("TrackedPose"))
            {
                hasTrackedPoseDriver = true;
                Debug.Log("Found existing Tracked Pose Driver: " + typeName);
                break;
            }
        }
        
        if (!hasTrackedPoseDriver)
        {
            Debug.Log("Tracked Pose Driver must be added manually through the inspector.");
            Debug.Log("Please select the AR Camera object and add: Add Component > XR > Tracked Pose Driver (Input System)");
            
            // Select the camera so it's easy to add the component
            Selection.activeGameObject = arCameraObj;
        }
        
        // Find ARWallSystem and update its camera reference
        ARWallSystem arWallSystem = FindObjectOfType<ARWallSystem>();
        if (arWallSystem != null)
        {
            arWallSystem.CameraManager = cameraManager;
            Debug.Log("Updated ARWallSystem camera reference");
        }
        
        Debug.Log("AR Camera setup complete. If warning persists, please add Tracked Pose Driver component manually.");
    }
#endif

    public static void ConfigureARCamera()
    {
        // Find XR Origin in the scene
        XROrigin xrOrigin = Object.FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("No XR Origin found in the scene.");
            return;
        }

        // Find Camera Offset
        GameObject cameraOffset = xrOrigin.CameraFloorOffsetObject;
        if (cameraOffset == null)
        {
            cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform, false);
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
        }

        // Check if AR Camera already exists
        Camera arCamera = xrOrigin.Camera;
        GameObject arCameraObj = null;
        
        if (arCamera != null)
        {
            arCameraObj = arCamera.gameObject;
        }
        else
        {
            // Look for AR Camera under Camera Offset
            foreach (Transform child in cameraOffset.transform)
            {
                Camera childCamera = child.GetComponent<Camera>();
                if (childCamera != null)
                {
                    arCameraObj = child.gameObject;
                    arCamera = childCamera;
                    break;
                }
            }
            
            // If still not found, create a new AR Camera
            if (arCameraObj == null)
            {
                arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffset.transform, false);
                arCamera = arCameraObj.AddComponent<Camera>();
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
            }
            
            // Set camera in XR Origin
            xrOrigin.Camera = arCamera;
        }
        
        // Add required AR components
        if (!arCameraObj.TryGetComponent<ARCameraManager>(out _))
        {
            arCameraObj.AddComponent<ARCameraManager>();
        }
        
        if (!arCameraObj.TryGetComponent<ARCameraBackground>(out _))
        {
            arCameraObj.AddComponent<ARCameraBackground>();
        }
        
        // Update ARWallSystem reference
        ARWallSystem arWallSystem = Object.FindObjectOfType<ARWallSystem>();
        if (arWallSystem != null)
        {
            arWallSystem.CameraManager = arCameraObj.GetComponent<ARCameraManager>();
        }
    }
} 