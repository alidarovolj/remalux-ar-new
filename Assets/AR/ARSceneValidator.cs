using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// Validates and fixes AR scene setup at runtime to ensure components are properly connected
/// </summary>
public class ARSceneValidator : MonoBehaviour
{
    [Header("Required Components")]
    [SerializeField] private ARSession arSession;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private MLManager mlManager;
    [SerializeField] private ARMLController armlController;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    [SerializeField] private WallColorizer wallColorizer;
    
    [Header("UI Components")]
    [SerializeField] private Text statusText;
    [SerializeField] private Button scanButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private RawImage displayImage;
    
    [Header("Settings")]
    [SerializeField] private bool validateOnAwake = true;
    // Commented out unused field
    // [SerializeField] private bool fixMissingReferences = true;
    
    // Add camera reference at class level to fix the 'arCamera does not exist' errors
    private Camera arCamera;
    // Add a reference to ARManager as it's used in FixComponentReferences
    private ARManager arManager;
    
    private void Awake()
    {
        if (validateOnAwake)
        {
            ValidateScene();
        }
    }
    
    /// <summary>
    /// Validates AR scene setup and fixes any missing references or connections
    /// </summary>
    public void ValidateScene()
    {
        try
        {
            Debug.Log("AR Scene Validator: Starting scene validation...");
            
            // Find all AR components in scene
            FindAllComponents();
            
            // Validate camera setup
            ValidateCameraSetup();
            
            // Fix component references
            FixComponentReferences();
            
            // Ensure UI is properly set up
            ValidateUI();
            
            Debug.Log("AR Scene Validator: Scene validation complete - no critical errors found");
            
            // Автоматически запускаем AR после валидации сцены
            StartCoroutine(AutoStartARAfterDelay());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"AR Scene Validator: Error during validation: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Автоматически запускает AR после короткой задержки
    /// </summary>
    private IEnumerator AutoStartARAfterDelay()
    {
        // Небольшая задержка для завершения всех инициализаций
        yield return new WaitForSeconds(1.5f);
        
        if (armlController != null)
        {
            Debug.Log("AR Scene Validator: Auto-starting AR without needing SCAN button");
            armlController.StartAR();
            
            // Задержка перед переключением в режим размещения
            yield return new WaitForSeconds(2.0f);
            
            // Находим PlaceDetector и PlaceDetectorUI
            PlaceDetector placeDetector = FindFirstObjectByType<PlaceDetector>();
            PlaceDetectorUI placeDetectorUI = FindFirstObjectByType<PlaceDetectorUI>();
            
            // Запускаем сканирование
            if (placeDetector != null)
            {
                placeDetector.StartScanning();
                Debug.Log("AR Scene Validator: Auto-started scanning");
                
                // Ждем немного для сканирования поверхностей
                yield return new WaitForSeconds(3.0f);
            }
            
            // Переключаемся в режим размещения через UI для согласованности
            if (placeDetectorUI != null)
            {
                placeDetectorUI.SwitchToPlacementMode();
                Debug.Log("AR Scene Validator: Auto-switched to placement mode");
            }
            // Если UI не найден, делаем то же самое через PlaceDetector
            else if (placeDetector != null)
            {
                placeDetector.StopScanning();
                placeDetector.TogglePlacementMode(true);
                Debug.Log("AR Scene Validator: Auto-enabled placement mode directly");
            }
        }
    }
    
    /// <summary>
    /// Find all required components in the scene
    /// </summary>
    private void FindAllComponents()
    {
        armlController = FindFirstObjectByType<ARMLController>();
        mlManager = FindFirstObjectByType<MLManager>();
        arSession = FindFirstObjectByType<ARSession>();
        deepLabPredictor = FindFirstObjectByType<DeepLabPredictor>();
        wallColorizer = FindFirstObjectByType<WallColorizer>();
        arManager = FindFirstObjectByType<ARManager>();
        
        // Find camera with increased reliability
        arCamera = Camera.main;
        if (arCamera == null)
        {
            // Try to find AR Camera directly
            GameObject cameraObj = GameObject.Find("AR Camera");
            if (cameraObj != null)
            {
                arCamera = cameraObj.GetComponent<Camera>();
            }
            
            // Last resort - find any camera
            if (arCamera == null)
            {
                arCamera = FindFirstObjectByType<Camera>();
            }
        }
        
        // Find display image with increased reliability
        GameObject displayObj = GameObject.Find("AR Display");
        if (displayObj != null)
        {
            displayImage = displayObj.GetComponent<RawImage>();
        }
        
        if (displayImage == null)
        {
            // Try to find through canvas hierarchy
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                Transform displayTransform = canvas.transform.Find("AR Display");
                if (displayTransform != null)
                {
                    displayImage = displayTransform.GetComponent<RawImage>();
                    if (displayImage != null) break;
                }
            }
            
            // Last resort - find any RawImage
            if (displayImage == null)
            {
                displayImage = FindFirstObjectByType<RawImage>();
            }
        }
    }
    
    /// <summary>
    /// Ensure XROrigin has correct camera reference
    /// </summary>
    private void ValidateCameraSetup()
    {
        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null && arCamera != null)
        {
            if (xrOrigin.Camera == null || xrOrigin.Camera != arCamera)
            {
                xrOrigin.Camera = arCamera;
                Debug.Log("AR Scene Validator: Fixed XROrigin camera reference");
            }
            
            // Ensure camera offset is set
            if (xrOrigin.CameraFloorOffsetObject == null)
            {
                // Try to find Camera Offset
                Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
                if (cameraOffset != null)
                {
                    xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                    Debug.Log("AR Scene Validator: Fixed XROrigin camera offset reference");
                }
                else
                {
                    // Create camera offset if missing
                    GameObject offsetObj = new GameObject("Camera Offset");
                    offsetObj.transform.SetParent(xrOrigin.transform, false);
                    xrOrigin.CameraFloorOffsetObject = offsetObj;
                    
                    // Move camera to offset
                    if (arCamera.transform.parent != offsetObj.transform)
                    {
                        arCamera.transform.SetParent(offsetObj.transform, true);
                    }
                    Debug.Log("AR Scene Validator: Created missing Camera Offset object");
                }
            }
        }
    }
    
    /// <summary>
    /// Fix component references between AR and ML systems
    /// </summary>
    private void FixComponentReferences()
    {
        // Fix ARMLController references
        if (armlController != null)
        {
            // Use reflection to set private fields
            var arManagerField = typeof(ARMLController).GetField("arManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (arManagerField != null && arManager != null)
            {
                arManagerField.SetValue(armlController, arManager);
            }
            
            var mlManagerField = typeof(ARMLController).GetField("mlManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mlManagerField != null && mlManager != null)
            {
                mlManagerField.SetValue(armlController, mlManager);
            }
            
            var deepLabPredictorField = typeof(ARMLController).GetField("deepLabPredictor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (deepLabPredictorField != null && deepLabPredictor != null)
            {
                deepLabPredictorField.SetValue(armlController, deepLabPredictor);
            }
            
            var wallColorizerField = typeof(ARMLController).GetField("wallColorizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (wallColorizerField != null && wallColorizer != null)
            {
                wallColorizerField.SetValue(armlController, wallColorizer);
            }
            
            Debug.Log("AR Scene Validator: Fixed ARMLController references");
        }
        
        // Fix MLManager references
        if (mlManager != null)
        {
            var deepLabPredictorField = typeof(MLManager).GetField("deepLabPredictor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (deepLabPredictorField != null && deepLabPredictor != null)
            {
                deepLabPredictorField.SetValue(mlManager, deepLabPredictor);
            }
            
            Debug.Log("AR Scene Validator: Fixed MLManager references");
        }
        
        // Fix WallColorizer references - CRITICAL
        if (wallColorizer != null)
        {
            if (wallColorizer.displayImage == null && displayImage != null)
            {
                wallColorizer.displayImage = displayImage;
                Debug.Log("AR Scene Validator: Fixed WallColorizer display image reference");
            }
            
            if (wallColorizer.arCamera == null && arCamera != null)
            {
                wallColorizer.arCamera = arCamera;
                Debug.Log("AR Scene Validator: Fixed WallColorizer camera reference");
            }
        }
        
        // Connect button actions if missing
        Button scanButton = GameObject.Find("Scan Button")?.GetComponent<Button>();
        if (scanButton != null && armlController != null)
        {
            // Check if button doesn't have listeners
            if (scanButton.onClick.GetPersistentEventCount() == 0)
            {
                scanButton.onClick.AddListener(armlController.StartAR);
                Debug.Log("AR Scene Validator: Added StartAR listener to Scan button");
            }
        }
        
        Button resetButton = GameObject.Find("Reset Button")?.GetComponent<Button>();
        if (resetButton != null && armlController != null)
        {
            // Check if button doesn't have listeners
            if (resetButton.onClick.GetPersistentEventCount() == 0)
            {
                resetButton.onClick.AddListener(armlController.StopAR);
                Debug.Log("AR Scene Validator: Added StopAR listener to Reset button");
            }
        }
    }
    
    /// <summary>
    /// Validate and fix UI elements
    /// </summary>
    private void ValidateUI()
    {
        // Ensure display image is set up
        if (displayImage != null)
        {
            // Give it a black texture if none assigned to prevent errors
            if (displayImage.texture == null)
            {
                displayImage.texture = Texture2D.blackTexture;
                Debug.Log("AR Scene Validator: Set default texture on AR Display");
            }
            
            // Make sure RawImage is correctly sized
            RectTransform rect = displayImage.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }
        else
        {
            // Create AR Display image if it doesn't exist
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                // Create a canvas if one doesn't exist
                GameObject canvasGO = new GameObject("AR UI Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                Debug.Log("AR Scene Validator: Created new Canvas for AR Display");
            }
            
            // Create display image
            GameObject displayGO = new GameObject("AR Display");
            displayGO.transform.SetParent(canvas.transform, false);
            displayImage = displayGO.AddComponent<RawImage>();
            
            // Set up RectTransform to fill the screen
            RectTransform rect = displayImage.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling(); // Put it behind other UI elements
            
            // Set default texture
            displayImage.texture = Texture2D.blackTexture;
            
            // Set reference in WallColorizer
            if (wallColorizer != null)
            {
                wallColorizer.displayImage = displayImage;
            }
            
            Debug.Log("AR Scene Validator: Created new AR Display image");
        }
    }
} 