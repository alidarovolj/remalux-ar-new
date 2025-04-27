using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils; // Добавляем для XROrigin

public static class ARSceneSetup
{
    /// <summary>
    /// Ensures all necessary AR components are present in the scene
    /// </summary>
    public static void SetupARScene()
    {
        // Find or create AR Session
        ARSession arSession = FindOrCreate<ARSession>("AR Session");
        
        // Find or create AR Session Origin
        GameObject sessionOriginObj = FindOrCreateGameObject("AR Session Origin");
        ARSessionOrigin sessionOrigin = sessionOriginObj.GetComponent<ARSessionOrigin>();
        if (sessionOrigin == null)
        {
            sessionOrigin = sessionOriginObj.AddComponent<ARSessionOrigin>();
            Debug.Log("ARSceneSetup: Added ARSessionOrigin component");
        }
        
        // Make sure AR Session Origin has a camera
        if (sessionOrigin.camera == null)
        {
            // Find camera in children
            Camera cam = sessionOriginObj.GetComponentInChildren<Camera>();
            if (cam == null)
            {
                // Create AR Camera
                GameObject cameraObj = new GameObject("AR Camera");
                cameraObj.transform.SetParent(sessionOriginObj.transform);
                cam = cameraObj.AddComponent<Camera>();
                cameraObj.AddComponent<ARCameraManager>();
                cameraObj.AddComponent<ARCameraBackground>();
                Debug.Log("ARSceneSetup: Created AR Camera with managers");
            }
            sessionOrigin.camera = cam;
        }
        
        // Add or find ARPlaneManager
        ARPlaneManager planeManager = sessionOriginObj.GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = sessionOriginObj.AddComponent<ARPlaneManager>();
            Debug.Log("ARSceneSetup: Added ARPlaneManager component");
        }
        
        // Add or find ARRaycastManager
        ARRaycastManager raycastManager = sessionOriginObj.GetComponent<ARRaycastManager>();
        if (raycastManager == null)
        {
            raycastManager = sessionOriginObj.AddComponent<ARRaycastManager>();
            Debug.Log("ARSceneSetup: Added ARRaycastManager component");
        }
        
        // Add or find ARAnchorManager
        ARAnchorManager anchorManager = sessionOriginObj.GetComponent<ARAnchorManager>();
        if (anchorManager == null)
        {
            anchorManager = sessionOriginObj.AddComponent<ARAnchorManager>();
            Debug.Log("ARSceneSetup: Added ARAnchorManager component");
        }
        
        // Add or find ARMeshManager 
        ARMeshManager meshManager = sessionOriginObj.GetComponent<ARMeshManager>();
        if (meshManager == null)
        {
            meshManager = sessionOriginObj.AddComponent<ARMeshManager>();
            Debug.Log("ARSceneSetup: Added ARMeshManager component");
        }
        
        // Now add our ARWallAnchor component to handle wall anchoring
        ARWallAnchor wallAnchor = sessionOriginObj.GetComponent<ARWallAnchor>();
        if (wallAnchor == null)
        {
            wallAnchor = sessionOriginObj.AddComponent<ARWallAnchor>();
            Debug.Log("ARSceneSetup: Added ARWallAnchor component");
        }
        
        // Add WallAnchorManager to the ARScene object
        GameObject arSceneObj = GameObject.Find("ARScene*");
        if (arSceneObj == null)
        {
            // Try other possible names
            arSceneObj = GameObject.Find("ARScene");
            
            if (arSceneObj == null)
            {
                // Create it if it doesn't exist
                arSceneObj = new GameObject("ARScene");
                Debug.Log("ARSceneSetup: Created ARScene object");
            }
        }
        
        // Add the WallAnchorManager to ARScene
        WallAnchorManager wallAnchorManager = arSceneObj.GetComponent<WallAnchorManager>();
        if (wallAnchorManager == null)
        {
            wallAnchorManager = arSceneObj.AddComponent<WallAnchorManager>();
            Debug.Log("ARSceneSetup: Added WallAnchorManager to ARScene");
        }
        
        // Create an UI button for wall anchoring
        SetupWallAnchoringUI(arSceneObj);
        
        Debug.Log("ARSceneSetup: Setup complete with wall anchoring support!");
    }
    
    /// <summary>
    /// Set up UI for wall anchoring
    /// </summary>
    private static void SetupWallAnchoringUI(GameObject arSceneObj)
    {
        // Find or create Canvas
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        GameObject canvasObj;
        
        if (canvas == null)
        {
            canvasObj = new GameObject("UI Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Debug.Log("ARSceneSetup: Created UI Canvas");
        }
        else
        {
            canvasObj = canvas.gameObject;
        }
        
        // Find or create anchor button
        Transform existingButton = canvasObj.transform.Find("AnchorWallsButton");
        
        if (existingButton == null)
        {
            // Create the button
            GameObject buttonObj = new GameObject("AnchorWallsButton");
            buttonObj.transform.SetParent(canvasObj.transform, false);
            
            // Set up rect transform
            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.05f, 0.05f);
            rectTransform.anchorMax = new Vector2(0.25f, 0.1f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            
            // Add image component (background)
            UnityEngine.UI.Image image = buttonObj.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.2f, 0.4f, 0.8f, 0.8f);
            
            // Add button component
            UnityEngine.UI.Button button = buttonObj.AddComponent<UnityEngine.UI.Button>();
            
            // Add text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            
            RectTransform textRectTransform = textObj.AddComponent<RectTransform>();
            textRectTransform.anchorMin = Vector2.zero;
            textRectTransform.anchorMax = Vector2.one;
            textRectTransform.offsetMin = Vector2.zero;
            textRectTransform.offsetMax = Vector2.zero;
            
            UnityEngine.UI.Text text = textObj.AddComponent<UnityEngine.UI.Text>();
            text.text = "Закрепить стены";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            
            // Add button script
            WallAnchorButton anchorButton = buttonObj.AddComponent<WallAnchorButton>();
            
            Debug.Log("ARSceneSetup: Created wall anchoring button");
        }
    }
    
    /// <summary>
    /// Find a component of the specified type, or create it if it doesn't exist
    /// </summary>
    private static T FindOrCreate<T>(string name) where T : Component
    {
        T component = Object.FindObjectOfType<T>();
        if (component == null)
        {
            GameObject obj = new GameObject(name);
            component = obj.AddComponent<T>();
            Debug.Log($"ARSceneSetup: Created {name} with {typeof(T).Name}");
        }
        return component;
    }
    
    /// <summary>
    /// Find a GameObject with the specified name, or create it if it doesn't exist
    /// </summary>
    private static GameObject FindOrCreateGameObject(string name)
    {
        GameObject obj = GameObject.Find(name);
        if (obj == null)
        {
            obj = new GameObject(name);
            Debug.Log($"ARSceneSetup: Created {name} GameObject");
        }
        return obj;
    }
} 