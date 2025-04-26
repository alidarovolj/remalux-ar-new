using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using System;
using UnityEditor.SceneManagement;

/// <summary>
/// Modified version of ARSceneSetupBasic that creates a camera-space UI Canvas and
/// properly sets up AR camera for native background rendering.
/// </summary>
public static class ARSceneSetupBasicFix
{
    [MenuItem("AR/Setup AR Scene (Native Camera)")]
    public static void SetupARSceneWithNativeCamera()
    {
        // Create a new scene
        Scene newScene = CreateARScene();
        
        // Add required AR components
        GameObject arSystem = CreateARComponents();
        
        // Find the XROrigin and AR Camera
        XROrigin existingXROrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
        Camera arCamera = existingXROrigin?.Camera;
        
        if (existingXROrigin == null || arCamera == null)
        {
            Debug.LogError("Failed to set up AR correctly - XROrigin or AR Camera not found");
            return;
        }
        
        // Ensure AR Camera has ARCameraManager and ARCameraBackground components
        EnsureARCameraComponents(arCamera.gameObject);
        
        // Create UI Canvas in camera space
        GameObject uiCanvas = CreateARUICanvas(arCamera);
        
        // Check and fix other components
        FixARMeshManagerHierarchy(existingXROrigin);
        
        // Save the scene
        string scenePath = EditorUtility.SaveFilePanel("Save AR Scene", "Assets", "ARScene", "unity");
        if (!string.IsNullOrEmpty(scenePath))
        {
            string relativePath = scenePath.Substring(Application.dataPath.Length - "Assets".Length);
            EditorSceneManager.SaveScene(newScene, relativePath);
            Debug.Log("AR scene created and saved to: " + relativePath);
        }
        else
        {
            Debug.Log("AR scene created but not saved.");
        }
    }
    
    /// <summary>
    /// Creates a new scene for AR
    /// </summary>
    private static Scene CreateARScene()
    {
        // Create a new empty scene
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // Add main light
        GameObject directionalLight = new GameObject("Directional Light");
        Light light = directionalLight.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(1.0f, 0.95f, 0.84f);
        directionalLight.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        return newScene;
    }
    
    /// <summary>
    /// Creates the AR components with correct hierarchy
    /// </summary>
    private static GameObject CreateARComponents()
    {
        // Create a parent container for AR System
        GameObject arSystem = new GameObject("AR System");
        
        // Setup AR Session
        GameObject arSessionObj = new GameObject("AR Session");
        arSessionObj.transform.SetParent(arSystem.transform, false);
        ARSession arSession = arSessionObj.AddComponent<ARSession>();
        arSessionObj.AddComponent<ARInputManager>();
        
        // Create XR Origin
        GameObject xrOriginObj = new GameObject("XR Origin");
        xrOriginObj.transform.SetParent(arSystem.transform, false);
        XROrigin xrOrigin = xrOriginObj.AddComponent<XROrigin>();
        
        // Add AR Raycast Manager to XR Origin
        xrOriginObj.AddComponent<ARRaycastManager>();
        
        // Create Camera Offset
        GameObject cameraOffsetObj = new GameObject("Camera Offset");
        cameraOffsetObj.transform.SetParent(xrOriginObj.transform, false);
        
        // Create AR Camera
        GameObject arCameraObj = new GameObject("AR Camera");
        arCameraObj.transform.SetParent(cameraOffsetObj.transform, false);
        
        // Setup camera
        Camera arCamera = arCameraObj.AddComponent<Camera>();
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.1f;
        arCamera.farClipPlane = 20f;
        arCameraObj.tag = "MainCamera";
        
        // Set up XROrigin
        xrOrigin.Camera = arCamera;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
        
        // Create AR Mesh Manager as child of XR Origin
        GameObject meshManagerObj = new GameObject("AR Mesh Manager");
        meshManagerObj.transform.SetParent(xrOrigin.transform, false);
        ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
        meshManager.density = 0.5f;
        
        return arSystem;
    }
    
    /// <summary>
    /// Ensures AR Camera has required components
    /// </summary>
    private static void EnsureARCameraComponents(GameObject arCameraObj)
    {
        // Add ARCameraManager
        if (arCameraObj.GetComponent<ARCameraManager>() == null)
        {
            ARCameraManager cameraManager = arCameraObj.AddComponent<ARCameraManager>();
            Debug.Log("Added ARCameraManager to AR Camera");
            
            // Configure auto-focus for mobile devices
            #if UNITY_IOS || UNITY_ANDROID
            cameraManager.autoFocusRequested = true;
            #endif
        }
        
        // Add ARCameraBackground
        if (arCameraObj.GetComponent<ARCameraBackground>() == null)
        {
            arCameraObj.AddComponent<ARCameraBackground>();
            Debug.Log("Added ARCameraBackground to AR Camera");
        }
    }
    
    /// <summary>
    /// Creates the AR UI Canvas with camera-space rendering
    /// </summary>
    private static GameObject CreateARUICanvas(Camera arCamera)
    {
        // Create Canvas for UI
        GameObject canvasObj = new GameObject("UI Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = arCamera;
        canvas.planeDistance = 1.0f;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create color panel at the bottom of the screen
        GameObject colorPanelObj = new GameObject("Color Panel");
        colorPanelObj.transform.SetParent(canvasObj.transform, false);
        
        RectTransform colorPanelRect = colorPanelObj.AddComponent<RectTransform>();
        colorPanelRect.anchorMin = new Vector2(0, 0);
        colorPanelRect.anchorMax = new Vector2(1, 0.1f);
        colorPanelRect.offsetMin = new Vector2(10, 10);
        colorPanelRect.offsetMax = new Vector2(-10, -10);
        
        Image colorPanelImage = colorPanelObj.AddComponent<Image>();
        colorPanelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        
        // Create color buttons
        CreateColorButtons(colorPanelObj.transform);
        
        return canvasObj;
    }
    
    /// <summary>
    /// Creates color palette buttons
    /// </summary>
    private static void CreateColorButtons(Transform parent)
    {
        GameObject colorPalette = new GameObject("Color Palette");
        colorPalette.transform.SetParent(parent, false);
        
        RectTransform rect = colorPalette.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.6f, 0.1f);
        rect.anchorMax = new Vector2(0.95f, 0.9f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        
        // Define colors for the palette
        Color[] colors = new Color[]
        {
            new Color(1f, 1f, 1f, 1f),      // White
            new Color(0.8f, 0.8f, 0.8f, 1f), // Light gray
            new Color(0.5f, 0.8f, 1f, 1f),   // Light blue
            new Color(0.2f, 0.6f, 1f, 1f),   // Blue
            new Color(0.8f, 0.5f, 1f, 1f),   // Purple
            new Color(1f, 0.5f, 0.5f, 1f),   // Pink
            new Color(1f, 0.8f, 0.2f, 1f),   // Yellow
            new Color(0.5f, 0.8f, 0.2f, 1f)  // Green
        };
        
        // Calculate grid layout
        int columns = 4;
        int rows = (colors.Length + columns - 1) / columns;
        float buttonWidth = 1f / columns;
        float buttonHeight = 1f / rows;
        
        // Create color buttons
        for (int i = 0; i < colors.Length; i++)
        {
            int row = i / columns;
            int col = i % columns;
            
            // Calculate anchor positions
            Vector2 anchorMin = new Vector2(col * buttonWidth, 1f - (row + 1) * buttonHeight);
            Vector2 anchorMax = new Vector2((col + 1) * buttonWidth, 1f - row * buttonHeight);
            
            GameObject colorButton = new GameObject($"Color_{i}");
            colorButton.transform.SetParent(colorPalette.transform, false);
            
            RectTransform buttonRect = colorButton.AddComponent<RectTransform>();
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            buttonRect.offsetMin = new Vector2(2, 2);
            buttonRect.offsetMax = new Vector2(-2, -2);
            
            Image buttonImage = colorButton.AddComponent<Image>();
            buttonImage.color = colors[i];
            
            Button button = colorButton.AddComponent<Button>();
            
            // Store color in button name
            colorButton.name = $"ColorButton_{colors[i].r}_{colors[i].g}_{colors[i].b}";
        }
    }
    
    /// <summary>
    /// Fixes the AR Mesh Manager hierarchy
    /// </summary>
    private static void FixARMeshManagerHierarchy(XROrigin xrOrigin)
    {
        if (xrOrigin == null)
        {
            Debug.LogError("Cannot fix AR Mesh Manager hierarchy - XR Origin not found");
            return;
        }
        
        // Check existing AR Mesh Managers
        ARMeshManager[] allManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None);
        bool hasCorrectMeshManager = false;
        
        foreach (ARMeshManager meshManager in allManagers)
        {
            if (meshManager.transform.parent == xrOrigin.transform)
            {
                hasCorrectMeshManager = true;
                break;
            }
            else
            {
                // Remove incorrectly placed mesh managers
                GameObject.DestroyImmediate(meshManager.gameObject);
                Debug.Log("Removed incorrectly placed AR Mesh Manager");
            }
        }
        
        if (!hasCorrectMeshManager)
        {
            // Create a new AR Mesh Manager
            GameObject meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
            meshManager.density = 0.5f;
            Debug.Log("Created new AR Mesh Manager as child of XR Origin");
        }
    }

    private static void EnsureCorrectHierarchy()
    {
        // Find or create AR System
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            arSystem = new GameObject("AR System");
            Debug.Log("Created AR System game object");
        }

        // Find XROrigin
        XROrigin existingXROrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
        if (existingXROrigin == null)
        {
            Debug.LogWarning("XROrigin not found in the scene. Please add XROrigin first.");
        }
        else
        {
            // ... existing code ...
        }

        // ... existing code ...

        // Check for AR Mesh Managers
        ARMeshManager[] allManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None);
        if (allManagers.Length > 0)
        {
            // ... existing code ...
        }
        
        // ... existing code ...
    }
} 