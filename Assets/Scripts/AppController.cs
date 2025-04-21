using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using System.Collections;

public class AppController : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private ARSession arSession;
    [SerializeField] private RawImage arCameraFeedImage;
    
    [Header("Wall Detection")]
    [SerializeField] private WallDetectionManager wallDetector;
    [SerializeField] private DeepLabPredictor deepLabPredictor;
    
    [Header("Visualization")]
    [SerializeField] private WallColorizer wallColorizer;
    [SerializeField] private Button[] colorButtons; // Простые кнопки выбора цвета вместо ColorPicker
    
    [Header("UI Settings")]
    [SerializeField] private Slider opacitySlider;
    [SerializeField] private Toggle enableARPlaneVisualizationToggle;
    
    [Header("Processing Settings")]
    [SerializeField] private float processingInterval = 1.0f;
    private bool isProcessing = false;
    private Texture2D lastCameraTexture;
    
    private ARPlaneManager planeManager;
    
    private void Start()
    {
        // Find components if not assigned
        if (arCameraManager == null)
            arCameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            
        if (arSession == null)
            arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
            
        if (wallDetector == null)
            wallDetector = FindFirstObjectByType<WallDetectionManager>(FindObjectsInactive.Include);
            
        if (deepLabPredictor == null)
            deepLabPredictor = FindFirstObjectByType<DeepLabPredictor>(FindObjectsInactive.Include);
            
        if (wallColorizer == null)
            wallColorizer = FindFirstObjectByType<WallColorizer>(FindObjectsInactive.Include);
            
        planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        
        // Setup UI events
        if (opacitySlider != null)
            opacitySlider.onValueChanged.AddListener(SetWallOpacity);
            
        // Настройка цветовых кнопок
        SetupColorButtons();
            
        if (enableARPlaneVisualizationToggle != null && planeManager != null)
            enableARPlaneVisualizationToggle.onValueChanged.AddListener(ToggleARPlaneVisualization);
        
        // Initialize processing - always start auto scan
        StartCoroutine(AutoScanRoutine());
    }
    
    private void SetupColorButtons()
    {
        if (colorButtons == null || colorButtons.Length == 0)
            return;
            
        // Настраиваем каждую кнопку выбора цвета
        foreach (var button in colorButtons)
        {
            if (button == null) continue;
            
            // Получаем цвет из компонента Image кнопки
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
            {
                Color buttonColor = buttonImage.color;
                button.onClick.AddListener(() => SetWallColor(buttonColor));
            }
        }
    }
    
    private void OnDestroy()
    {
        // Clean up UI events
        if (opacitySlider != null)
            opacitySlider.onValueChanged.RemoveListener(SetWallOpacity);
            
        // Очистка событий цветовых кнопок
        if (colorButtons != null)
        {
            foreach (var button in colorButtons)
            {
                if (button != null)
                {
                    Image buttonImage = button.GetComponent<Image>();
                    if (buttonImage != null)
                    {
                        Color buttonColor = buttonImage.color;
                        button.onClick.RemoveListener(() => SetWallColor(buttonColor));
                    }
                }
            }
        }
            
        if (enableARPlaneVisualizationToggle != null && planeManager != null)
            enableARPlaneVisualizationToggle.onValueChanged.RemoveListener(ToggleARPlaneVisualization);
    }
    
    private IEnumerator AutoScanRoutine()
    {
        while (true) // Всегда сканируем, независимо от значения autoScanEnabled
        {
            if (!isProcessing)
                yield return StartCoroutine(ProcessWallDetection());
                
            yield return new WaitForSeconds(processingInterval);
        }
    }
    
    private IEnumerator ProcessWallDetection()
    {
        isProcessing = true;
        
        // Capture current camera frame
        lastCameraTexture = CaptureARCameraTexture();
        
        if (lastCameraTexture != null)
        {
            // Option 1: Process with DeepLabV3 directly
            if (deepLabPredictor != null)
            {
                RenderTexture wallMask = deepLabPredictor.PredictSegmentation(lastCameraTexture);
                if (wallColorizer != null && wallMask != null)
                {
                    // Используем метод из нового класса WallColorizer
                    wallColorizer.SetWallMask(wallMask);
                }
            }
            // Option 2: Use WallDetectionManager for full processing with OpenCV
            else if (wallDetector != null)
            {
                // The wall detector handles the processing internally
                // and creates 3D wall visualizers
            }
        }
        
        yield return null;
        isProcessing = false;
    }
    
    private Texture2D CaptureARCameraTexture()
    {
        if (arCameraFeedImage != null && arCameraFeedImage.texture != null)
        {
            // Copy the current AR camera feed to a texture
            RenderTexture rt = arCameraFeedImage.texture as RenderTexture;
            if (rt != null)
            {
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                return tex;
            }
        }
        
        Debug.LogWarning("Could not capture AR camera texture");
        return null;
    }
    
    // Метод для внутреннего использования, не привязанный к кнопке
    private void ResetWallPainting()
    {
        if (wallColorizer != null)
        {
            wallColorizer.ClearWalls();
        }
    }
    
    public void SetWallColor(Color color)
    {
        if (wallColorizer != null)
        {
            wallColorizer.SetColor(color);
        }
    }
    
    public void SetWallOpacity(float opacity)
    {
        if (wallColorizer != null)
        {
            wallColorizer.SetOpacity(opacity);
        }
    }
    
    public void ToggleARPlaneVisualization(bool enabled)
    {
        if (planeManager != null)
        {
            planeManager.enabled = enabled;
            
            // Toggle visibility of existing planes
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(enabled);
            }
        }
    }
} 