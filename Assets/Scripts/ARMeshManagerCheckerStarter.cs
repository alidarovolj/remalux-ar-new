using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Component that automatically creates and runs ARMeshManager checker at startup
/// </summary>
public class ARMeshManagerCheckerStarter : MonoBehaviour
{
    public XROrigin targetXROrigin;
    
    [Tooltip("Whether to check on startup")]
    public bool checkOnStart = true;
    
    void Start()
    {
        if (checkOnStart)
        {
            CheckAndFixARMeshManager();
        }
    }
    
    public void CheckAndFixARMeshManager()
    {
        if (targetXROrigin == null)
        {
            // Try to find XROrigin in the scene
            targetXROrigin = FindObjectOfType<XROrigin>();
            
            if (targetXROrigin == null)
            {
                Debug.LogError("ARMeshManagerCheckerStarter: XROrigin reference is missing!");
                return;
            }
        }
        
        // Check if ARMeshManager exists and is in the correct hierarchy
        ARMeshManager meshManager = targetXROrigin.GetComponentInChildren<ARMeshManager>();
        if (meshManager == null)
        {
            // Create a new ARMeshManager under XROrigin
            GameObject meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(targetXROrigin.transform);
            meshManagerObj.transform.localPosition = Vector3.zero;
            meshManagerObj.transform.localRotation = Quaternion.identity;
            meshManagerObj.transform.localScale = Vector3.one;
            
            meshManager = meshManagerObj.AddComponent<ARMeshManager>();
            meshManager.density = 0.5f;
            
            Debug.Log("ARMeshManagerCheckerStarter: Created new ARMeshManager under XROrigin");
        }
        else if (meshManager.transform.parent != targetXROrigin.transform)
        {
            // ARMeshManager exists but has incorrect parent, move it
            Transform originalParent = meshManager.transform.parent;
            string originalPath = GetGameObjectPath(meshManager.gameObject);
            
            meshManager.transform.SetParent(targetXROrigin.transform);
            meshManager.transform.localPosition = Vector3.zero;
            meshManager.transform.localRotation = Quaternion.identity;
            meshManager.transform.localScale = Vector3.one;
            
            Debug.Log($"ARMeshManagerCheckerStarter: Moved ARMeshManager from {originalPath} to be direct child of XROrigin");
        }
        else
        {
            Debug.Log("ARMeshManagerCheckerStarter: ARMeshManager already exists with correct hierarchy");
        }
    }
    
    // Helper method to get full path of GameObject in hierarchy
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }
} 