using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using TMPro;
using UnityEditor;

/// <summary>
/// Скрипт для автоматической настройки полной сцены AR Wall Detection
/// </summary>
public class ARWallDetectionSetup : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private GameObject arSystemPrefab;
    [SerializeField] private GameObject uiCanvasPrefab;
    
    [Header("References")]
    [SerializeField] private RawImage displayImage;
    [SerializeField] private Button scanButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private TextMeshProUGUI statusText;
    
    public void SetupCompleteScene()
    {
        Debug.Log("Начинаем настройку полной AR сцены...");
        
        // Проверяем и настраиваем AR компоненты
        SetupARComponents();
        
        // Настраиваем UI
        SetupUI();
        
        // Настраиваем связи между компонентами
        SetupReferences();
        
        Debug.Log("Настройка AR сцены завершена успешно!");
    }
    
    private void SetupARComponents()
    {
        // Удаляем существующие AR компоненты, которые могут быть дублированы
        CleanupExistingComponents();
        
        // Создаем основную AR систему, если она еще не существует
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            if (arSystemPrefab != null)
            {
                arSystem = Instantiate(arSystemPrefab);
                arSystem.name = "AR System";
            }
            else
            {
                arSystem = new GameObject("AR System");
                
                // Добавляем AR Session
                GameObject arSessionObj = new GameObject("AR Session");
                arSessionObj.transform.SetParent(arSystem.transform);
                ARSession arSessionComponent = arSessionObj.AddComponent<ARSession>();
                // Создаем XROrigin вместо устаревшего ARSessionOrigin
                arSessionObj.AddComponent<ARInputManager>();
                
                // Добавляем XR Origin
                GameObject xrOriginObj = new GameObject("XR Origin");
                xrOriginObj.transform.SetParent(arSystem.transform);
                XROrigin xrOrigin = xrOriginObj.AddComponent<XROrigin>();
                
                // Добавляем Camera Offset
                GameObject cameraOffsetObj = new GameObject("Camera Offset");
                cameraOffsetObj.transform.SetParent(xrOriginObj.transform);
                cameraOffsetObj.transform.localPosition = Vector3.zero;
                xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
                
                // Добавляем AR Camera
                GameObject arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffsetObj.transform);
                arCameraObj.transform.localPosition = Vector3.zero;
                Camera arCamera = arCameraObj.AddComponent<Camera>();
                arCamera.tag = "MainCamera";
                arCameraObj.AddComponent<ARCameraManager>();
                arCameraObj.AddComponent<ARCameraBackground>();
                xrOrigin.Camera = arCamera;
                
                // Добавляем AR Features
                GameObject arFeaturesObj = new GameObject("AR Features");
                arFeaturesObj.transform.SetParent(arSystem.transform);
                ARPlaneManager planeManager = arFeaturesObj.AddComponent<ARPlaneManager>();
                ARRaycastManager raycastManager = arFeaturesObj.AddComponent<ARRaycastManager>();
                
                // Добавляем ML System
                GameObject mlSystemObj = new GameObject("ML System");
                mlSystemObj.transform.SetParent(arSystem.transform);
                
                // Добавляем DeepLab Predictor
                GameObject deepLabObj = new GameObject("DeepLab Predictor");
                deepLabObj.transform.SetParent(mlSystemObj.transform);
                DeepLabPredictor deepLabPredictor = deepLabObj.AddComponent<DeepLabPredictor>();
                
                // Добавляем ML Manager
                GameObject mlManagerObj = new GameObject("ML Manager");
                mlManagerObj.transform.SetParent(mlSystemObj.transform);
                MLManager mlManager = mlManagerObj.AddComponent<MLManager>();
                
                // Добавляем Wall Colorizer
                GameObject wallColorizerObj = new GameObject("Wall Colorizer");
                wallColorizerObj.transform.SetParent(mlSystemObj.transform);
                WallColorizer wallColorizer = wallColorizerObj.AddComponent<WallColorizer>();
                
                // Добавляем ARML Controller
                GameObject armlControllerObj = new GameObject("ARML Controller");
                armlControllerObj.transform.SetParent(mlSystemObj.transform);
                ARMLController armlController = armlControllerObj.AddComponent<ARMLController>();
                
                // Добавляем AR Scene Validator
                GameObject arSceneValidatorObj = new GameObject("AR Scene Validator");
                arSceneValidatorObj.transform.SetParent(arSystem.transform);
                ARSceneValidator arSceneValidator = arSceneValidatorObj.AddComponent<ARSceneValidator>();
            }
        }
        
        // Настройка AR Session Helper
        ARSessionHelper sessionHelper = FindOrCreateComponent<ARSessionHelper>("AR Session");
        ARSession arSession = FindOrCreateComponent<ARSession>("AR Session");

        // Устанавливаем ссылку на ARSession, если есть публичный доступ
        if (sessionHelper != null && arSession != null)
        {
            // Используем reflection для установки поля, если оно приватное
            var fieldInfo = typeof(ARSessionHelper).GetField("arSession", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(sessionHelper, arSession);
            }
        }
    }
    
    private void SetupUI()
    {
        // Находим или создаем UI Canvas
        Canvas uiCanvas = Object.FindFirstObjectByType<Canvas>();
        if (uiCanvas == null)
        {
            if (uiCanvasPrefab != null)
            {
                GameObject uiCanvasObj = Instantiate(uiCanvasPrefab);
                uiCanvasObj.name = "UI Canvas";
                uiCanvas = uiCanvasObj.GetComponent<Canvas>();
            }
            else
            {
                GameObject uiCanvasObj = new GameObject("UI Canvas");
                uiCanvas = uiCanvasObj.AddComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCanvasObj.AddComponent<CanvasScaler>();
                uiCanvasObj.AddComponent<GraphicRaycaster>();
                
                // Создаем Panel для UI элементов
                GameObject panelObj = new GameObject("Control Panel");
                panelObj.transform.SetParent(uiCanvas.transform, false);
                panelObj.AddComponent<RectTransform>().anchorMin = new Vector2(0, 0);
                panelObj.AddComponent<RectTransform>().anchorMax = new Vector2(1, 0.2f);
                panelObj.AddComponent<CanvasRenderer>();
                Image panelImage = panelObj.AddComponent<Image>();
                panelImage.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
                
                // Создаем AR Display (RawImage)
                GameObject displayObj = new GameObject("AR Display");
                displayObj.transform.SetParent(uiCanvas.transform, false);
                displayImage = displayObj.AddComponent<RawImage>();
                RectTransform displayRect = displayObj.GetComponent<RectTransform>();
                displayRect.anchorMin = Vector2.zero;
                displayRect.anchorMax = Vector2.one;
                displayRect.offsetMin = Vector2.zero;
                displayRect.offsetMax = Vector2.zero;
                displayRect.SetAsFirstSibling(); // Размещаем позади других UI элементов
                
                // Создаем Scan Button
                GameObject scanButtonObj = new GameObject("Scan Button");
                scanButtonObj.transform.SetParent(panelObj.transform, false);
                scanButton = scanButtonObj.AddComponent<Button>();
                RectTransform scanButtonRect = scanButtonObj.GetComponent<RectTransform>();
                scanButtonRect.anchorMin = new Vector2(0.05f, 0.1f);
                scanButtonRect.anchorMax = new Vector2(0.25f, 0.9f);
                scanButtonRect.offsetMin = Vector2.zero;
                scanButtonRect.offsetMax = Vector2.zero;
                scanButtonObj.AddComponent<CanvasRenderer>();
                Image scanButtonImage = scanButtonObj.AddComponent<Image>();
                scanButtonImage.color = new Color(0.2f, 0.6f, 1f, 1f);
                
                // Добавляем текст на кнопку Scan
                GameObject scanButtonTextObj = new GameObject("Text");
                scanButtonTextObj.transform.SetParent(scanButtonObj.transform, false);
                TextMeshProUGUI scanButtonText = scanButtonTextObj.AddComponent<TextMeshProUGUI>();
                scanButtonText.text = "SCAN";
                scanButtonText.color = Color.white;
                scanButtonText.alignment = TextAlignmentOptions.Center;
                scanButtonText.fontSize = 24;
                RectTransform scanButtonTextRect = scanButtonTextObj.GetComponent<RectTransform>();
                scanButtonTextRect.anchorMin = Vector2.zero;
                scanButtonTextRect.anchorMax = Vector2.one;
                scanButtonTextRect.offsetMin = Vector2.zero;
                scanButtonTextRect.offsetMax = Vector2.zero;
                
                // Создаем Clear Button
                GameObject clearButtonObj = new GameObject("Clear Button");
                clearButtonObj.transform.SetParent(panelObj.transform, false);
                clearButton = clearButtonObj.AddComponent<Button>();
                RectTransform clearButtonRect = clearButtonObj.GetComponent<RectTransform>();
                clearButtonRect.anchorMin = new Vector2(0.75f, 0.1f);
                clearButtonRect.anchorMax = new Vector2(0.95f, 0.9f);
                clearButtonRect.offsetMin = Vector2.zero;
                clearButtonRect.offsetMax = Vector2.zero;
                clearButtonObj.AddComponent<CanvasRenderer>();
                Image clearButtonImage = clearButtonObj.AddComponent<Image>();
                clearButtonImage.color = new Color(1f, 0.5f, 0.3f, 1f);
                
                // Добавляем текст на кнопку Clear
                GameObject clearButtonTextObj = new GameObject("Text");
                clearButtonTextObj.transform.SetParent(clearButtonObj.transform, false);
                TextMeshProUGUI clearButtonText = clearButtonTextObj.AddComponent<TextMeshProUGUI>();
                clearButtonText.text = "CLEAR";
                clearButtonText.color = Color.white;
                clearButtonText.alignment = TextAlignmentOptions.Center;
                clearButtonText.fontSize = 24;
                RectTransform clearButtonTextRect = clearButtonTextObj.GetComponent<RectTransform>();
                clearButtonTextRect.anchorMin = Vector2.zero;
                clearButtonTextRect.anchorMax = Vector2.one;
                clearButtonTextRect.offsetMin = Vector2.zero;
                clearButtonTextRect.offsetMax = Vector2.zero;
                
                // Создаем Status Text
                GameObject statusTextObj = new GameObject("Status Text");
                statusTextObj.transform.SetParent(panelObj.transform, false);
                statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
                statusText.text = "AR Status";
                statusText.color = Color.white;
                statusText.alignment = TextAlignmentOptions.Center;
                statusText.fontSize = 20;
                RectTransform statusTextRect = statusTextObj.GetComponent<RectTransform>();
                statusTextRect.anchorMin = new Vector2(0.3f, 0.1f);
                statusTextRect.anchorMax = new Vector2(0.7f, 0.9f);
                statusTextRect.offsetMin = Vector2.zero;
                statusTextRect.offsetMax = Vector2.zero;
            }
        }
    }
    
    private void SetupReferences()
    {
        // Прямой поиск компонентов по типу для повышения надежности
        ARSession arSession = Object.FindFirstObjectByType<ARSession>();
        ARSceneValidator arSceneValidator = Object.FindFirstObjectByType<ARSceneValidator>();
        ARPlaneManager planeManager = Object.FindFirstObjectByType<ARPlaneManager>();
        ARRaycastManager raycastManager = Object.FindFirstObjectByType<ARRaycastManager>();
        MLManager mlManager = Object.FindFirstObjectByType<MLManager>();
        DeepLabPredictor deepLabPredictor = Object.FindFirstObjectByType<DeepLabPredictor>();
        WallColorizer wallColorizer = Object.FindFirstObjectByType<WallColorizer>();
        ARMLController armlController = Object.FindFirstObjectByType<ARMLController>();
        
        // Находим UI компоненты
        if (statusText == null) statusText = Object.FindFirstObjectByType<GameObject>().GetComponentInChildren<TextMeshProUGUI>(true);
        if (scanButton == null) scanButton = Object.FindFirstObjectByType<GameObject>().GetComponentInChildren<Button>(true);
        if (clearButton == null) clearButton = GameObject.Find("Clear Button")?.GetComponent<Button>();
        if (displayImage == null) displayImage = GameObject.Find("AR Display")?.GetComponent<RawImage>();
        
        Debug.Log($"Найдены компоненты: " +
              $"ARSession: {(arSession != null)}, " +
              $"ARPlaneManager: {(planeManager != null)}, " +
              $"MLManager: {(mlManager != null)}, " +
              $"ARMLController: {(armlController != null)}, " +
              $"DeepLabPredictor: {(deepLabPredictor != null)}, " +
              $"WallColorizer: {(wallColorizer != null)}, " +
              $"StatusText: {(statusText != null)}, " +
              $"ScanButton: {(scanButton != null)}, " +
              $"DisplayImage: {(displayImage != null)}");
        
        // Настраиваем AR Scene Validator
        if (arSceneValidator != null)
        {
            // Прямая установка полей через SetField вместо использования SerializedObject
            SetField(arSceneValidator, "arSession", arSession);
            SetField(arSceneValidator, "planeManager", planeManager);
            SetField(arSceneValidator, "mlManager", mlManager);
            SetField(arSceneValidator, "armlController", armlController);
            SetField(arSceneValidator, "deepLabPredictor", deepLabPredictor);
            SetField(arSceneValidator, "wallColorizer", wallColorizer);
            SetField(arSceneValidator, "statusText", statusText);
            SetField(arSceneValidator, "scanButton", scanButton);
            SetField(arSceneValidator, "resetButton", clearButton); // Предполагаем, что resetButton = clearButton
            SetField(arSceneValidator, "displayImage", displayImage);
            
            Debug.Log("ARSceneValidator настроен напрямую через reflection.");
        }
        else
        {
            Debug.LogError("ARSceneValidator не найден! Невозможно настроить ссылки.");
        }
        
        // Настраиваем ML Manager
        if (mlManager != null && deepLabPredictor != null)
        {
            SetField(mlManager, "deepLabPredictor", deepLabPredictor);
        }
        
        // Настраиваем Wall Colorizer
        if (wallColorizer != null)
        {
            Camera arCamera = Object.FindFirstObjectByType<Camera>();
            SetField(wallColorizer, "displayImage", displayImage);
            SetField(wallColorizer, "arCamera", arCamera);
        }
        
        // Настраиваем ARML Controller
        if (armlController != null)
        {
            ARManager arManager = Object.FindFirstObjectByType<ARManager>();
            if (arManager == null)
            {
                GameObject arManagerObj = new GameObject("AR Manager");
                arManagerObj.transform.SetParent(GameObject.Find("AR System").transform);
                arManager = arManagerObj.AddComponent<ARManager>();
            }
            
            SetField(armlController, "arManager", arManager);
            SetField(armlController, "mlManager", mlManager);
            SetField(armlController, "deepLabPredictor", deepLabPredictor);
            SetField(armlController, "wallColorizer", wallColorizer);
            
            // Устанавливаем autoStartAR в true
            SetField(armlController, "autoStartAR", true);
        }
        
        // Создаем и настраиваем PlaceDetector и PlaceDetectorUI, если нужно
        SetupPlaceDetector(Object.FindFirstObjectByType<Camera>(), planeManager, raycastManager);
    }
    
    // Вспомогательный метод для установки значения поля через reflection
    private void SetField<T, V>(T target, string fieldName, V value)
    {
        if (target == null)
        {
            Debug.LogError($"Целевой объект null при установке поля {fieldName}");
            return;
        }
        
        // Пытаемся найти и установить публичное поле
        var field = typeof(T).GetField(fieldName);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }
        
        // Пытаемся найти и установить приватное или защищенное поле
        field = typeof(T).GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }
        
        // Пытаемся найти и использовать сеттер свойства
        var property = typeof(T).GetProperty(fieldName);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return;
        }
        
        Debug.LogWarning($"Поле или свойство {fieldName} не найдено в {typeof(T).Name}");
    }
    
    private void SetupPlaceDetector(Camera arCamera, ARPlaneManager planeManager, ARRaycastManager raycastManager)
    {
        // Находим или создаем PlaceDetector
        PlaceDetector placeDetector = Object.FindFirstObjectByType<PlaceDetector>();
        if (placeDetector == null)
        {
            GameObject placeDetectorObj = new GameObject("Place Detector");
            placeDetector = placeDetectorObj.AddComponent<PlaceDetector>();
        }
        
        // Настраиваем PlaceDetector
        if (placeDetector != null)
        {
            SetField(placeDetector, "raycastManager", raycastManager);
            SetField(placeDetector, "planeManager", planeManager);
            
            ARSessionHelper sessionHelper = Object.FindFirstObjectByType<ARSessionHelper>();
            if (sessionHelper != null)
            {
                SetField(placeDetector, "sessionHelper", sessionHelper);
            }
        }
        
        // Находим или создаем PlaceDetectorUI
        PlaceDetectorUI placeDetectorUI = Object.FindFirstObjectByType<PlaceDetectorUI>();
        if (placeDetectorUI == null)
        {
            GameObject uiObj = GameObject.Find("UI Canvas");
            if (uiObj != null)
            {
                placeDetectorUI = uiObj.AddComponent<PlaceDetectorUI>();
            }
        }
        
        // Настраиваем PlaceDetectorUI
        if (placeDetectorUI != null)
        {
            SetField(placeDetectorUI, "placeDetector", placeDetector);
            
            ARSessionHelper sessionHelper = Object.FindFirstObjectByType<ARSessionHelper>();
            if (sessionHelper != null)
            {
                SetField(placeDetectorUI, "sessionHelper", sessionHelper);
            }
            
            SetField(placeDetectorUI, "scanButton", scanButton);
            SetField(placeDetectorUI, "statusText", statusText);
            SetField(placeDetectorUI, "clearButton", clearButton);
            
            // Включаем автостарт
            SetField(placeDetectorUI, "autoStartEnabled", true);
        }
    }
    
    private void CleanupExistingComponents()
    {
        // Проверяем дублирование XR Origin и удаляем лишние копии
        XROrigin[] xrOrigins = Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        if (xrOrigins.Length > 1)
        {
            Debug.Log($"Найдено {xrOrigins.Length} компонентов XR Origin. Оставляем только один.");
            
            // Оставляем только XR Origin на объекте AR System/XR Origin
            foreach (XROrigin origin in xrOrigins)
            {
                if (origin.gameObject.name != "XR Origin" || 
                    origin.transform.parent == null || 
                    origin.transform.parent.name != "AR System")
                {
                    DestroyImmediate(origin);
                }
            }
        }
    }
    
    private T FindOrCreateComponent<T>(string objectName) where T : Component
    {
        // Сначала ищем существующий компонент
        T[] components = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        foreach (T component in components)
        {
            if (component.gameObject.name == objectName || component.gameObject.name.Contains(objectName))
            {
                return component;
            }
        }
        
        // Если не найдено, возвращаем null (компонент будет создан в другом месте)
        return null;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ARWallDetectionSetup))]
public class ARWallDetectionSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        ARWallDetectionSetup setup = (ARWallDetectionSetup)target;
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Setup Complete AR Wall Detection Scene", GUILayout.Height(40)))
        {
            setup.SetupCompleteScene();
        }
    }
}
#endif 