using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Utility script to fix AR Camera setup issues
/// </summary>
public class ARCameraSetup : MonoBehaviour
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

    public static void EnsureARCameraExists()
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