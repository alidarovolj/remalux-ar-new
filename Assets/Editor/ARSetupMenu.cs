using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

public class ARSetupMenu : Editor
{
    [MenuItem("AR Wall Detection/Setup Default Components")]
    public static void SetupARComponents()
    {
        // Check if AR Session already exists
        if (Object.FindFirstObjectByType<ARSession>() == null)
        {
            GameObject arSession = new GameObject("AR Session");
            arSession.AddComponent<ARSession>();
            Debug.Log("AR Session added to scene");
        }
        else
        {
            Debug.Log("AR Session already exists in the scene");
        }

        // Check if XR Origin exists
        if (Object.FindFirstObjectByType<XROrigin>() == null)
        {
            // Create XR Origin with Camera
            GameObject xrOrigin = new GameObject("XR Origin");
            XROrigin origin = xrOrigin.AddComponent<XROrigin>();
            
            // Create Camera Offset
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);
            
            // Create AR Camera
            GameObject arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(cameraOffset.transform);
            Camera camera = arCamera.AddComponent<Camera>();
            arCamera.AddComponent<ARCameraManager>();
            arCamera.AddComponent<ARCameraBackground>();
            
            // Setup origin
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = cameraOffset;
            
            Debug.Log("XR Origin added to scene");
        }
        else
        {
            Debug.Log("XR Origin already exists in the scene");
        }
        
        // Add AR Ray Manager
        if (Object.FindFirstObjectByType<ARRaycastManager>() == null)
        {
            GameObject xrOrigin = Object.FindFirstObjectByType<XROrigin>().gameObject;
            xrOrigin.AddComponent<ARRaycastManager>();
            Debug.Log("AR Raycast Manager added to XR Origin");
        }
        
        // Add AR Plane Manager
        if (Object.FindFirstObjectByType<ARPlaneManager>() == null)
        {
            GameObject xrOrigin = Object.FindFirstObjectByType<XROrigin>().gameObject;
            xrOrigin.AddComponent<ARPlaneManager>();
            Debug.Log("AR Plane Manager added to XR Origin");
        }
        
        Debug.Log("AR components setup complete!");
    }

    [MenuItem("AR Wall Detection/Fix AR Components", false, 10)]
    public static void FixARComponents()
    {
        // Check if the scene is empty
        if (EditorUtility.DisplayDialog("Fix AR Components", 
            "This action will add or fix AR components for wall detection in the current scene. Continue?", 
            "Yes", "Cancel"))
        {
            // Find XR Origin
            XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                EditorUtility.DisplayDialog("Error", 
                    "No XROrigin found in the scene. Please create an AR scene first.", 
                    "OK");
                return;
            }

            // Add the AR Component Fixer
            GameObject componentFixerObj = new GameObject("AR Component Fixer");
            ARComponentFixer fixer = componentFixerObj.AddComponent<ARComponentFixer>();
            
            // Configure the fixer
            SerializedObject serializedFixer = new SerializedObject(fixer);
            var originProp = serializedFixer.FindProperty("arSessionOrigin");
            if (originProp != null)
            {
                originProp.objectReferenceValue = xrOrigin;
            }
            serializedFixer.ApplyModifiedProperties();
            
            // Try to find the wall material
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMesh.mat");
            if (wallMaterial != null)
            {
                fixer.wallMaterial = wallMaterial;
            }
            else
            {
                Debug.LogWarning("WallMesh material not found at Assets/Materials/WallMesh.mat");
            }
            
            // Run the fixer
            fixer.FixAllComponents();
            
            // Select the fixer object
            Selection.activeGameObject = componentFixerObj;
            
            EditorUtility.DisplayDialog("Success", 
                "AR Component Fixer has been added to the scene and components have been configured for wall detection.", 
                "OK");
        }
    }
    
    [MenuItem("AR Wall Detection/Fix AR Components", true)]
    public static bool ValidateFixARComponents()
    {
        // Check if there's an XR Origin in the scene
        return Object.FindFirstObjectByType<XROrigin>() != null;
    }
    
    [MenuItem("AR Wall Detection/Create Material for Walls", false, 20)]
    public static void CreateWallMaterial()
    {
        // Check if the material already exists
        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMesh.mat");
        if (existingMaterial != null)
        {
            if (!EditorUtility.DisplayDialog("Material Already Exists", 
                "A WallMesh material already exists. Do you want to replace it?", 
                "Yes", "Cancel"))
            {
                return;
            }
        }
        
        // Create Materials folder if it doesn't exist
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        
        // Create a new material
        Material wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.name = "WallMesh";
        
        // Make it semi-transparent blue
        Color wallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
        wallMaterial.color = wallColor;
        wallMaterial.SetFloat("_Mode", 3); // Transparent mode
        wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wallMaterial.SetInt("_ZWrite", 0);
        wallMaterial.DisableKeyword("_ALPHATEST_ON");
        wallMaterial.EnableKeyword("_ALPHABLEND_ON");
        wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        wallMaterial.renderQueue = 3000;
        
        // Save the material as an asset
        AssetDatabase.CreateAsset(wallMaterial, "Assets/Materials/WallMesh.mat");
        AssetDatabase.SaveAssets();
        
        EditorUtility.DisplayDialog("Success", 
            "WallMesh material has been created at Assets/Materials/WallMesh.mat", 
            "OK");
        
        // Select the created material in the Project window
        Selection.activeObject = wallMaterial;
    }
    
    [MenuItem("AR Wall Detection/Add Wall Visualization Controls", false, 30)]
    public static void AddWallControlButtons()
    {
        // Check if a UI Canvas exists in the scene
        Canvas mainCanvas = Object.FindFirstObjectByType<Canvas>();
        if (mainCanvas == null)
        {
            // Create a new Canvas
            GameObject canvasObj = new GameObject("UI Canvas");
            mainCanvas = canvasObj.AddComponent<Canvas>();
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            // Add CanvasScaler
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            
            // Add GraphicRaycaster
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            
            Debug.Log("Created new UI Canvas for controls");
        }
        
        // Find WallColorizer
        WallColorizer wallColorizer = Object.FindFirstObjectByType<WallColorizer>();
        if (wallColorizer == null)
        {
            EditorUtility.DisplayDialog("Error", 
                "No WallColorizer found in the scene. Please set up the AR Wall Detection first.", 
                "OK");
            return;
        }
        
        // Create a control panel
        GameObject controlPanel = new GameObject("Wall Controls");
        controlPanel.transform.SetParent(mainCanvas.transform, false);
        
        // Add panel component
        UnityEngine.UI.Image panelImage = controlPanel.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f);
        
        // Set panel position and size
        RectTransform panelRect = controlPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(0, 0);
        panelRect.pivot = new Vector2(0, 0);
        panelRect.anchoredPosition = new Vector2(10, 10);
        panelRect.sizeDelta = new Vector2(200, 160);
        
        // Add vertical layout group
        UnityEngine.UI.VerticalLayoutGroup layoutGroup = controlPanel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);
        layoutGroup.spacing = 8;
        
        // Create toggle for depth test
        CreateToggle(controlPanel, "Use Depth Test", wallColorizer.gameObject, "ToggleDepthTest");
        
        // Create toggle for stabilization
        CreateToggle(controlPanel, "Stabilize Walls", wallColorizer.gameObject, "ToggleStabilization");
        
        // Create button to clear walls
        CreateButton(controlPanel, "Clear Walls", wallColorizer.gameObject, "ClearVisualization");
        
        EditorUtility.DisplayDialog("Success", 
            "Wall visualization control buttons have been added to the UI.", 
            "OK");
    }
    
    private static void CreateToggle(GameObject parent, string label, GameObject target, string methodName)
    {
        // Create toggle GameObject
        GameObject toggleObj = new GameObject(label);
        toggleObj.transform.SetParent(parent.transform, false);
        
        // Add layout element
        UnityEngine.UI.LayoutElement layoutElement = toggleObj.AddComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minHeight = 30;
        layoutElement.flexibleWidth = 1;
        
        // Add horizontal layout
        UnityEngine.UI.HorizontalLayoutGroup hLayout = toggleObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        hLayout.spacing = 5;
        
        // Create toggle component
        GameObject toggleControl = new GameObject("Toggle");
        toggleControl.transform.SetParent(toggleObj.transform, false);
        UnityEngine.UI.Toggle toggle = toggleControl.AddComponent<UnityEngine.UI.Toggle>();
        
        // Create background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(toggleControl.transform, false);
        UnityEngine.UI.Image bgImage = background.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = Color.white;
        
        // Set background rect
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.5f);
        bgRect.anchorMax = new Vector2(0, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(20, 20);
        
        // Create checkmark
        GameObject checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(background.transform, false);
        UnityEngine.UI.Image checkImage = checkmark.AddComponent<UnityEngine.UI.Image>();
        checkImage.color = Color.blue;
        
        // Set checkmark rect
        RectTransform checkRect = checkmark.GetComponent<RectTransform>();
        checkRect.anchorMin = Vector2.zero;
        checkRect.anchorMax = Vector2.one;
        checkRect.sizeDelta = new Vector2(-4, -4);
        
        // Configure toggle component
        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
        toggle.isOn = true;
        
        // Create label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(toggleObj.transform, false);
        UnityEngine.UI.Text labelText = labelObj.AddComponent<UnityEngine.UI.Text>();
        labelText.text = label;
        labelText.color = Color.white;
        labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleLeft;
        
        // Set label rect
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(30, 0);
        labelRect.offsetMax = Vector2.zero;
        
        // Add OnValueChanged event
        UnityEngine.Events.UnityAction<bool> toggleAction = (value) => 
        {
            var method = target.GetType().GetMethod(methodName);
            if (method != null)
            {
                method.Invoke(target, new object[] { value });
            }
        };
        
        // Set up the event through SerializedObject
        SerializedObject serializedToggle = new SerializedObject(toggle);
        SerializedProperty onValueChanged = serializedToggle.FindProperty("m_OnValueChanged");
        
        // Clear existing listeners
        onValueChanged.FindPropertyRelative("m_PersistentCalls.m_Calls").ClearArray();
        
        // Add new listener
        int index = onValueChanged.FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize;
        onValueChanged.FindPropertyRelative("m_PersistentCalls.m_Calls").InsertArrayElementAtIndex(index);
        
        SerializedProperty listener = onValueChanged.FindPropertyRelative("m_PersistentCalls.m_Calls").GetArrayElementAtIndex(index);
        listener.FindPropertyRelative("m_Target").objectReferenceValue = target;
        listener.FindPropertyRelative("m_MethodName").stringValue = methodName;
        listener.FindPropertyRelative("m_Mode").enumValueIndex = 0; // Bool argument
        
        serializedToggle.ApplyModifiedProperties();
    }
    
    private static void CreateButton(GameObject parent, string label, GameObject target, string methodName)
    {
        // Create button GameObject
        GameObject buttonObj = new GameObject(label);
        buttonObj.transform.SetParent(parent.transform, false);
        
        // Add layout element
        UnityEngine.UI.LayoutElement layoutElement = buttonObj.AddComponent<UnityEngine.UI.LayoutElement>();
        layoutElement.minHeight = 30;
        layoutElement.flexibleWidth = 1;
        
        // Add button component
        UnityEngine.UI.Button button = buttonObj.AddComponent<UnityEngine.UI.Button>();
        
        // Add image component
        UnityEngine.UI.Image buttonImage = buttonObj.AddComponent<UnityEngine.UI.Image>();
        buttonImage.color = new Color(0.2f, 0.2f, 0.8f);
        
        // Set button colors
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.2f, 0.8f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.9f);
        colors.pressedColor = new Color(0.1f, 0.1f, 0.7f);
        colors.selectedColor = new Color(0.2f, 0.2f, 0.8f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;
        
        // Create label
        GameObject labelObj = new GameObject("Text");
        labelObj.transform.SetParent(buttonObj.transform, false);
        UnityEngine.UI.Text labelText = labelObj.AddComponent<UnityEngine.UI.Text>();
        labelText.text = label;
        labelText.color = Color.white;
        labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleCenter;
        
        // Set label rect
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        
        // Add onClick event
        UnityEngine.Events.UnityAction buttonAction = () => 
        {
            var method = target.GetType().GetMethod(methodName);
            if (method != null)
            {
                method.Invoke(target, null);
            }
        };
        
        // Set up the event through SerializedObject
        SerializedObject serializedButton = new SerializedObject(button);
        SerializedProperty onClick = serializedButton.FindProperty("m_OnClick");
        
        // Clear existing listeners
        onClick.FindPropertyRelative("m_PersistentCalls.m_Calls").ClearArray();
        
        // Add new listener
        int index = onClick.FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize;
        onClick.FindPropertyRelative("m_PersistentCalls.m_Calls").InsertArrayElementAtIndex(index);
        
        SerializedProperty listener = onClick.FindPropertyRelative("m_PersistentCalls.m_Calls").GetArrayElementAtIndex(index);
        listener.FindPropertyRelative("m_Target").objectReferenceValue = target;
        listener.FindPropertyRelative("m_MethodName").stringValue = methodName;
        listener.FindPropertyRelative("m_Mode").enumValueIndex = 1; // No arguments
        
        serializedButton.ApplyModifiedProperties();
    }
} 