using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Editor menu items for ML System configuration
/// </summary>
public static class MLSystemMenuItems
{
    [MenuItem("GameObject/AR/ML System", false, 10)]
    public static void CreateMLSystem(MenuCommand menuCommand)
    {
        // Find AR System parent
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            // Create AR System if it doesn't exist
            arSystem = new GameObject("AR System");
            
            // Try to find and reorganize existing AR components
            XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            ARSession arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetParent(arSystem.transform, true);
            }
            
            if (arSession != null)
            {
                arSession.transform.SetParent(arSystem.transform, true);
            }
        }
        
        // Check if ML System already exists
        Transform existingMLSystem = arSystem.transform.Find("ML System");
        if (existingMLSystem != null)
        {
            // Select existing ML System
            Selection.activeGameObject = existingMLSystem.gameObject;
            return;
        }
        
        // Create ML System parent GameObject
        GameObject mlSystem = new GameObject("ML System");
        GameObjectUtility.SetParentAndAlign(mlSystem, arSystem);
        
        // Create SegmentationManager
        GameObject segmentationManagerObj = new GameObject("SegmentationManager");
        GameObjectUtility.SetParentAndAlign(segmentationManagerObj, mlSystem);
        segmentationManagerObj.AddComponent<SegmentationManager>();
        
        // Create MaskProcessor
        GameObject maskProcessorObj = new GameObject("MaskProcessor");
        GameObjectUtility.SetParentAndAlign(maskProcessorObj, mlSystem);
        maskProcessorObj.AddComponent<MaskProcessor>();
        
        // Create MLConnector
        GameObject mlConnectorObj = new GameObject("MLConnector");
        GameObjectUtility.SetParentAndAlign(mlConnectorObj, mlSystem);
        MLConnector mlConnector = mlConnectorObj.AddComponent<MLConnector>();
        
        // Find AR Camera Manager
        ARCameraManager cameraManager = UnityEngine.Object.FindFirstObjectByType<ARCameraManager>();
        if (cameraManager != null && mlConnector != null)
        {
            // Use SerializedObject to set reference
            SerializedObject serializedConnector = new SerializedObject(mlConnector);
            SerializedProperty cameraProp = serializedConnector.FindProperty("arCameraManager");
            if (cameraProp != null)
            {
                cameraProp.objectReferenceValue = cameraManager;
                serializedConnector.ApplyModifiedProperties();
            }
        }
        
        // Register undo operation
        Undo.RegisterCreatedObjectUndo(mlSystem, "Create ML System");
        
        // Select the created ML System
        Selection.activeGameObject = mlSystem;
    }
    
    [MenuItem("GameObject/AR/ML Debug Viewer", false, 11)]
    public static void CreateMLDebugViewer(MenuCommand menuCommand)
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
            Selection.activeGameObject = existingViewer.gameObject;
            return;
        }
        
        // Create ML Debug Viewer
        GameObject mlDebugViewer = new GameObject("ML Debug Viewer");
        GameObjectUtility.SetParentAndAlign(mlDebugViewer, uiCanvas);
        
        // Add RectTransform
        RectTransform rectTransform = mlDebugViewer.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.7f, 0.7f);
        rectTransform.anchorMax = new Vector2(0.95f, 0.95f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        // Add RawImage
        UnityEngine.UI.RawImage rawImage = mlDebugViewer.AddComponent<UnityEngine.UI.RawImage>();
        rawImage.color = Color.white;
        
        // Find MLConnector
        MLConnector mlConnector = UnityEngine.Object.FindFirstObjectByType<MLConnector>();
        if (mlConnector != null)
        {
            // Use SerializedObject to set reference
            SerializedObject serializedConnector = new SerializedObject(mlConnector);
            SerializedProperty debugImageProp = serializedConnector.FindProperty("debugRawImage");
            if (debugImageProp != null)
            {
                debugImageProp.objectReferenceValue = rawImage;
                serializedConnector.ApplyModifiedProperties();
            }
            
            // Enable debug mode
            SerializedProperty debugModeProp = serializedConnector.FindProperty("debugMode");
            if (debugModeProp != null)
            {
                debugModeProp.boolValue = true;
                serializedConnector.ApplyModifiedProperties();
            }
        }
        
        // Register undo operation
        Undo.RegisterCreatedObjectUndo(mlDebugViewer, "Create ML Debug Viewer");
        
        // Select the created viewer
        Selection.activeGameObject = mlDebugViewer;
    }
} 