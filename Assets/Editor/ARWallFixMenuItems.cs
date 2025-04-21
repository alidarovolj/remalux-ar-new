using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Provides menu items for AR Wall Detection setup
/// </summary>
public class ARWallFixMenuItems : MonoBehaviour
{
    private const string MENU_BASE = "AR Wall Detection/";
    
    [MenuItem(MENU_BASE + "Add Wall Detection Fix Tools")]
    public static void AddARWallFixTools()
    {
        // Create the parent game object
        GameObject fixTools = new GameObject("AR Wall Fix Tools");
        
        // Add helper components
        fixTools.AddComponent<ARFixHelper>();
        fixTools.AddComponent<ARPlaneHelper>();
        
        // Add WallMeshSetup
        var wallMeshSetup = fixTools.AddComponent<WallMeshSetup>();
        wallMeshSetup.setupOnStart = true;
        wallMeshSetup.syncClassIdWithPredictor = true;
        
        // Set debug and sensitivity settings
        wallMeshSetup.wallConfidenceThreshold = 0.3f;
        wallMeshSetup.verticalThreshold = 0.6f;
        
        Debug.Log("Added AR Wall Detection Fix Tools to scene");
        
        // Select the newly created object
        Selection.activeGameObject = fixTools;
        
        // Position at the top of hierarchy
        fixTools.transform.SetAsFirstSibling();
    }
    
    [MenuItem(MENU_BASE + "Fix Wall Class IDs")]
    public static void SynchronizeWallClassIDs()
    {
        // Find DeepLabPredictor first to get the wall class ID
        DeepLabPredictor predictor = Object.FindFirstObjectByType<DeepLabPredictor>();
        if (predictor == null)
        {
            Debug.LogError("DeepLabPredictor not found in scene!");
            return;
        }
        
        // Get wall class ID using reflection
        int wallClassId = 9; // Default value
        var property = typeof(DeepLabPredictor).GetProperty("WallClassId");
        if (property != null)
        {
            try
            {
                wallClassId = (int)property.GetValue(predictor);
                Debug.Log($"Found wall class ID in DeepLabPredictor: {wallClassId}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to get WallClassId from DeepLabPredictor: {e.Message}");
            }
        }
        
        // Find all WallMeshRenderer components
        WallMeshRenderer[] renderers = Object.FindObjectsByType<WallMeshRenderer>(FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            // Use reflection to set wall class ID
            var wallClassIdField = typeof(WallMeshRenderer).GetField("_wallClassId", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            if (wallClassIdField != null)
            {
                wallClassIdField.SetValue(renderer, wallClassId);
                EditorUtility.SetDirty(renderer);
                Debug.Log($"Set wall class ID to {wallClassId} on WallMeshRenderer");
            }
            else
            {
                Debug.LogWarning("Could not find _wallClassId field in WallMeshRenderer");
            }
        }
        
        // Find all WallDetectionOptimizer components
        var optimizers = Object.FindObjectsByType<WallDetectionOptimizer>(FindObjectsSortMode.None);
        foreach (var optimizer in optimizers)
        {
            // Use reflection to set the field value
            var field = typeof(WallDetectionOptimizer).GetField("wallClassId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                try
                {
                    field.SetValue(optimizer, wallClassId);
                    EditorUtility.SetDirty(optimizer);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to set wallClassId in WallDetectionOptimizer: {e.Message}");
                }
            }
        }
        
        Debug.Log($"Synchronized wall class ID ({wallClassId}) across all components");
    }
    
    [MenuItem(MENU_BASE + "Add ARPlaneManager")]
    public static void AddARPlaneManager()
    {
        // Find AR Features object
        GameObject arFeaturesObj = GameObject.Find("AR Features");
        if (arFeaturesObj == null)
        {
            // Try to find XR Origin
            var xrOrigin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("XR Origin not found in scene! Cannot add ARPlaneManager.");
                return;
            }
            
            // Create AR Features
            arFeaturesObj = new GameObject("AR Features");
            arFeaturesObj.transform.SetParent(xrOrigin.transform);
        }
        
        // Check if ARPlaneManager already exists
        ARPlaneManager planeManager = arFeaturesObj.GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            // Add ARPlaneManager
            planeManager = arFeaturesObj.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            planeManager.enabled = true;
            
            Debug.Log("Added ARPlaneManager to AR Features with Vertical detection mode");
        }
        else
        {
            // Update existing ARPlaneManager
            planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            Debug.Log("Updated existing ARPlaneManager to use Vertical detection mode");
        }
        
        // Connect to ARManager if possible
        ARManager arManager = Object.FindFirstObjectByType<ARManager>();
        if (arManager != null)
        {
            var field = typeof(ARManager).GetField("planeManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                try
                {
                    field.SetValue(arManager, planeManager);
                    EditorUtility.SetDirty(arManager);
                    Debug.Log("Connected ARPlaneManager to ARManager");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to connect ARPlaneManager to ARManager: {e.Message}");
                }
            }
        }
        
        // Select the AR Features object
        Selection.activeGameObject = arFeaturesObj;
    }
} 