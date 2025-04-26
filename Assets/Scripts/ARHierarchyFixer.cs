using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;

/// <summary>
/// Fixes AR component hierarchy at runtime to ensure proper structure
/// Specifically ensures AR Mesh Manager is a direct child of XR Origin and not under Camera Offset
/// </summary>
public class ARHierarchyFixer : MonoBehaviour
{
    [SerializeField] private bool fixOnStart = true;
    [SerializeField] private bool autoDestroySelfAfterFix = true;
    
    private void Start()
    {
        if (fixOnStart)
        {
            StartCoroutine(FixARHierarchyDelayed());
        }
    }
    
    private IEnumerator FixARHierarchyDelayed()
    {
        // Wait for one frame to ensure all components are initialized
        yield return null;
        
        FixARHierarchy();
        
        if (autoDestroySelfAfterFix)
        {
            Destroy(this);
        }
    }
    
    /// <summary>
    /// Fix AR component hierarchy to match AR Foundation best practices
    /// </summary>
    public void FixARHierarchy()
    {
        Debug.Log("ARHierarchyFixer: Starting hierarchy correction...");
        
        // Find XR Origin in scene
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("ARHierarchyFixer: No XR Origin found in the scene!");
            return;
        }
        
        // Get Camera Offset
        GameObject cameraOffset = xrOrigin.CameraFloorOffsetObject;
        if (cameraOffset == null)
        {
            Debug.LogWarning("ARHierarchyFixer: No Camera Offset found in XR Origin!");
            return;
        }
        
        // 1. Fix AR Mesh Manager position - should be direct child of XR Origin
        FixMeshManagerHierarchy(xrOrigin, cameraOffset);
        
        // 2. Fix other AR managers - make sure they're direct children of XR Origin
        FixOtherARManagersHierarchy(xrOrigin, cameraOffset);
        
        Debug.Log("ARHierarchyFixer: Hierarchy correction completed");
    }
    
    private void FixMeshManagerHierarchy(XROrigin xrOrigin, GameObject cameraOffset)
    {
        // Find all AR Mesh Managers in the scene
        var meshManagers = FindObjectsOfType<ARMeshManager>();
        
        foreach (var meshManager in meshManagers)
        {
            GameObject meshManagerObj = meshManager.gameObject;
            
            // Check if mesh manager is under Camera Offset (incorrect)
            if (meshManagerObj.transform.IsChildOf(cameraOffset.transform))
            {
                // Move it to be direct child of XR Origin
                Debug.Log("ARHierarchyFixer: Moving AR Mesh Manager from Camera Offset to XR Origin");
                meshManagerObj.transform.SetParent(xrOrigin.transform, true);
                
                // Restore local position to zero if it was set specifically
                meshManagerObj.transform.localPosition = Vector3.zero;
                meshManagerObj.transform.localRotation = Quaternion.identity;
            }
            else if (meshManagerObj.transform.parent != xrOrigin.transform)
            {
                // If it's not under XR Origin at all, move it there
                Debug.Log("ARHierarchyFixer: Moving AR Mesh Manager to XR Origin");
                meshManagerObj.transform.SetParent(xrOrigin.transform, true);
                meshManagerObj.transform.localPosition = Vector3.zero;
                meshManagerObj.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.Log("ARHierarchyFixer: AR Mesh Manager already correctly positioned under XR Origin");
            }
        }
    }
    
    private void FixOtherARManagersHierarchy(XROrigin xrOrigin, GameObject cameraOffset)
    {
        // Check ARPlaneManager
        FixManagerPosition<ARPlaneManager>(xrOrigin, cameraOffset, "AR Plane Manager");
        
        // Check ARAnchorManager
        FixManagerPosition<ARAnchorManager>(xrOrigin, cameraOffset, "AR Anchor Manager");
        
        // Check ARRaycastManager
        FixManagerPosition<ARRaycastManager>(xrOrigin, cameraOffset, "AR Raycast Manager");
        
        // Check other AR Foundation managers commonly used
        FixManagerPosition<AROcclusionManager>(xrOrigin, cameraOffset, "AR Occlusion Manager");
        FixManagerPosition<ARTrackedImageManager>(xrOrigin, cameraOffset, "AR Tracked Image Manager");
        FixManagerPosition<ARTrackedObjectManager>(xrOrigin, cameraOffset, "AR Tracked Object Manager");
    }
    
    private void FixManagerPosition<T>(XROrigin xrOrigin, GameObject cameraOffset, string managerName) where T : MonoBehaviour
    {
        var managers = FindObjectsOfType<T>();
        
        foreach (var manager in managers)
        {
            GameObject managerObj = manager.gameObject;
            
            // Check if manager is under Camera Offset (incorrect)
            if (managerObj.transform.IsChildOf(cameraOffset.transform))
            {
                // Move it to be direct child of XR Origin
                Debug.Log($"ARHierarchyFixer: Moving {managerName} from Camera Offset to XR Origin");
                managerObj.transform.SetParent(xrOrigin.transform, true);
                
                // Restore local position
                managerObj.transform.localPosition = Vector3.zero;
                managerObj.transform.localRotation = Quaternion.identity;
            }
            else if (managerObj.transform.parent != xrOrigin.transform && 
                     !managerObj.GetComponentInParent<ARWallSystem>() && // Skip if it's part of ARWallSystem
                     !managerObj.CompareTag("EditorOnly")) // Skip editor-only components
            {
                // If it's not under XR Origin at all, move it there
                Debug.Log($"ARHierarchyFixer: Moving {managerName} to XR Origin");
                managerObj.transform.SetParent(xrOrigin.transform, true);
                managerObj.transform.localPosition = Vector3.zero;
                managerObj.transform.localRotation = Quaternion.identity;
            }
        }
    }
} 