using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;

#if UNITY_EDITOR
/// <summary>
/// Editor utility for setting up the ML System in an AR scene
/// </summary>
public static class MLSystemSetup
{
    /// <summary>
    /// Creates the ML System GameObject and components
    /// </summary>
    [MenuItem("AR/Setup ML System")]
    public static void SetupMLSystem()
    {
        // Find AR System parent
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            Debug.LogError("AR System GameObject not found! Please set up AR scene first.");
            return;
        }
        
        // Check if ML System already exists
        Transform existingMLSystem = arSystem.transform.Find("ML System");
        if (existingMLSystem != null)
        {
            Debug.Log("ML System already exists. Updating configuration...");
            UpdateMLSystemConfig(existingMLSystem.gameObject);
            return;
        }
        
        // Create ML System parent GameObject
        GameObject mlSystem = new GameObject("ML System");
        mlSystem.transform.SetParent(arSystem.transform, false);
        
        // Create SegmentationManager
        GameObject segmentationManagerObj = new GameObject("SegmentationManager");
        segmentationManagerObj.transform.SetParent(mlSystem.transform, false);
        SegmentationManager segmentationManager = segmentationManagerObj.AddComponent<SegmentationManager>();
        
        // Create MaskProcessor
        GameObject maskProcessorObj = new GameObject("MaskProcessor");
        maskProcessorObj.transform.SetParent(mlSystem.transform, false);
        MaskProcessor maskProcessor = maskProcessorObj.AddComponent<MaskProcessor>();
        
        // Create MLConnector
        GameObject mlConnectorObj = new GameObject("MLConnector");
        mlConnectorObj.transform.SetParent(mlSystem.transform, false);
        ARMLSystemConnector mlConnector = mlConnectorObj.AddComponent<ARMLSystemConnector>();
        
        // Find AR Camera Manager
        ARCameraManager cameraManager = Object.FindAnyObjectByType<ARCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning("ARCameraManager not found. ML System may not function properly.");
        }
        
        // Find ARWallPainterController
        ARWallPainterController wallPainterController = Object.FindAnyObjectByType<ARWallPainterController>();
        if (wallPainterController == null)
        {
            Debug.LogWarning("ARWallPainterController not found. Creating a basic one...");
            
            // Create a basic ARWallPainterController
            GameObject wallPainterObj = new GameObject("ARWallPainterController");
            wallPainterObj.transform.SetParent(arSystem.transform, false);
            wallPainterController = wallPainterObj.AddComponent<ARWallPainterController>();
        }
        
        // Set up references
        if (mlConnector != null && segmentationManager != null && maskProcessor != null)
        {
            // Use reflection or direct assignment (if fields are public) to setup references
            Debug.Log("ML System setup completed. Please check Inspector to configure ML components.");
            
            // Select the ML System in the hierarchy for easy configuration
            Selection.activeGameObject = mlSystem;
        }
    }
    
    /// <summary>
    /// Updates an existing ML System configuration
    /// </summary>
    private static void UpdateMLSystemConfig(GameObject mlSystem)
    {
        // Update references and configuration of existing ML System
        ARMLSystemConnector mlConnector = mlSystem.GetComponentInChildren<ARMLSystemConnector>();
        SegmentationManager segmentationManager = mlSystem.GetComponentInChildren<SegmentationManager>();
        MaskProcessor maskProcessor = mlSystem.GetComponentInChildren<MaskProcessor>();
        
        if (mlConnector == null)
        {
            GameObject mlConnectorObj = new GameObject("MLConnector");
            mlConnectorObj.transform.SetParent(mlSystem.transform, false);
            mlConnector = mlConnectorObj.AddComponent<ARMLSystemConnector>();
        }
        
        if (segmentationManager == null)
        {
            GameObject segmentationManagerObj = new GameObject("SegmentationManager");
            segmentationManagerObj.transform.SetParent(mlSystem.transform, false);
            segmentationManager = segmentationManagerObj.AddComponent<SegmentationManager>();
        }
        
        if (maskProcessor == null)
        {
            GameObject maskProcessorObj = new GameObject("MaskProcessor");
            maskProcessorObj.transform.SetParent(mlSystem.transform, false);
            maskProcessor = maskProcessorObj.AddComponent<MaskProcessor>();
        }
        
        Debug.Log("ML System updated. Please check Inspector to configure ML components.");
        
        // Select the ML System in the hierarchy for easy configuration
        Selection.activeGameObject = mlSystem;
    }
    
    /// <summary>
    /// Adds a debug viewer UI for ML output
    /// </summary>
    [MenuItem("AR/Add ML Debug Viewer")]
    public static void AddMLDebugViewer()
    {
        // Find UI Canvas
        GameObject uiCanvas = GameObject.Find("UI Canvas");
        if (uiCanvas == null)
        {
            Debug.LogError("UI Canvas not found! Please set up AR scene first.");
            return;
        }
        
        // Check if debug viewer already exists
        Transform existingViewer = uiCanvas.transform.Find("ML Debug Viewer");
        if (existingViewer != null)
        {
            Debug.Log("ML Debug Viewer already exists.");
            Selection.activeGameObject = existingViewer.gameObject;
            return;
        }
        
        // Create ML Debug Viewer
        GameObject mlDebugViewer = new GameObject("ML Debug Viewer");
        mlDebugViewer.transform.SetParent(uiCanvas.transform, false);
        
        // Add RectTransform
        RectTransform rectTransform = mlDebugViewer.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.7f, 0.7f);
        rectTransform.anchorMax = new Vector2(0.95f, 0.95f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        // Add RawImage
        UnityEngine.UI.RawImage rawImage = mlDebugViewer.AddComponent<UnityEngine.UI.RawImage>();
        rawImage.color = Color.white;
        
        // Find MLConnector and set reference
        ARMLSystemConnector mlConnector = Object.FindAnyObjectByType<ARMLSystemConnector>();
        if (mlConnector != null)
        {
            // Use reflection to set the debugRawImage field
            System.Reflection.FieldInfo fieldInfo = mlConnector.GetType().GetField("debugRawImage", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(mlConnector, rawImage);
                Debug.Log("Set debug viewer reference in ARMLSystemConnector.");
            }
            
            // Enable debug mode
            System.Reflection.FieldInfo debugField = mlConnector.GetType().GetField("debugMode", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            if (debugField != null)
            {
                debugField.SetValue(mlConnector, true);
            }
        }
        
        Debug.Log("ML Debug Viewer added to UI Canvas.");
        Selection.activeGameObject = mlDebugViewer;
    }
}
#endif 