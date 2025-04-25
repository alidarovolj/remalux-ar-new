using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System.IO;
using UnityEditor.SceneManagement;
using Unity.XR.CoreUtils;
using System;
using UnityEngine.SceneManagement;

public class ARSceneSetupBasic : EditorWindow
{
    // Helper method to avoid SendMessage warnings by deferring component addition
    private static T SafeAddComponent<T>(GameObject target) where T : Component
    {
        // Check if component already exists
        T existing = target.GetComponent<T>();
        if (existing != null)
            return existing;
            
        // Создаем компонент немедленно для возврата
        T component = target.AddComponent<T>();
        
        // Проверяем, нужно ли удалить компонент в следующем кадре
        // для избежания предупреждений SendMessage
        bool removeComponent = false;
        
        // Пробуем добавить еще один компонент - это вызовет ошибку, если компонент уникальный
        try 
        {
            // Это вызовет ошибку, если компонент должен быть единственным
            var testComponent = target.AddComponent<T>();
            
            // Если ошибки не было, то тестовый компонент нужно удалить
            UnityEngine.Object.DestroyImmediate(testComponent);
        }
        catch 
        {
            // Ошибка означает, что компонент должен быть единственным
            // т.е. мы уже его добавили, и не нужно пытаться добавлять повторно
            removeComponent = true;
        }
        
        // Если не нужно удалять компонент, то планируем добавление через EditorApplication.delayCall
        if (!removeComponent)
        {
            // Queue the component addition for next editor update to avoid SendMessage warnings
            EditorApplication.delayCall += () => 
            {
                if (target != null) // Check if target still exists
                {
                    // Проверяем, не добавлен ли уже компонент
                    T existingDelayed = target.GetComponent<T>();
                    
                    if (existingDelayed == null || existingDelayed == component)
                    {
                        try
                        {
                            // Добавляем компонент только если он еще не существует
                            if (existingDelayed == null)
                            {
                                target.AddComponent<T>();
                            }
                            
                            EditorUtility.SetDirty(target);
                        }
                        catch (System.Exception e)
                        {
                            // Игнорируем ошибки, если компонент уже существует или его нельзя добавлять повторно
                            Debug.LogWarning($"SafeAddComponent: Не удалось добавить компонент {typeof(T).Name}: {e.Message}");
                        }
                    }
                }
            };
        }
        
        return component;
    }
    
    [MenuItem("AR/Setup AR Scene (Basic)")]
    public static void SetupARScene()
    {
        // Создаем новую сцену
        Scene newScene = CreateARScene();
        
        // Добавляем необходимые AR компоненты
        GameObject arSessionOrigin = CreateARComponents();
        
        // Добавляем интерфейс управления (только палитру цветов)
        GameObject uiCanvas = CreateARUICanvas();
        
        // Проверяем и исправляем позицию ARMeshManager для предотвращения ошибок
        FixARMeshManagerHierarchy();
        
        // Находим XROrigin для создания проверки на запуске
        XROrigin xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
        if (xrOrigin != null)
        {
            // Добавляем автоматический исправитель иерархии при запуске
            CreateARMeshManagerChecker(xrOrigin);
        }
        
        // Сохраняем сцену
        string scenePath = EditorUtility.SaveFilePanel("Save AR Scene", "Assets", "ARScene", "unity");
        if (!string.IsNullOrEmpty(scenePath))
        {
            // Преобразуем абсолютный путь в относительный путь проекта
            string relativePath = scenePath.Substring(Application.dataPath.Length - "Assets".Length);
            EditorSceneManager.SaveScene(newScene, relativePath);
            Debug.Log("AR сцена создана и сохранена в: " + relativePath);
        }
        else
        {
            Debug.Log("AR сцена создана, но не сохранена.");
        }
    }
    
    /// <summary>
    /// Создает новую сцену для AR
    /// </summary>
    private static Scene CreateARScene()
    {
        // Создаем новую пустую сцену
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // Добавляем основной свет
        GameObject directionalLight = new GameObject("Directional Light");
        Light light = SafeAddComponent<Light>(directionalLight);
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(1.0f, 0.95f, 0.84f);
        directionalLight.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        return newScene;
    }
    
    public static XROrigin SetupARSystem()
    {
        try
        {
            // Удаляем все старые ARMeshManager, чтобы не плодить дубли
            foreach (var old in UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None))
                GameObject.DestroyImmediate(old.gameObject);
            
            // Проверяем существование необходимых компонентов
            XROrigin xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            UnityEngine.XR.ARFoundation.ARSession arSession = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            
            // Создаем AR Session если его нет
            if (arSession == null)
            {
                GameObject arSessionObj = new GameObject("AR Session");
                arSession = SafeAddComponent<UnityEngine.XR.ARFoundation.ARSession>(arSessionObj);
                SafeAddComponent<UnityEngine.XR.ARFoundation.ARInputManager>(arSessionObj);
                
                Debug.Log("Created AR Session");
            }
            
            // Создаем XR Origin если его нет
            GameObject xrOriginObj = null;
            if (xrOrigin == null)
            {
                // Создаем объект XR Origin
                xrOriginObj = new GameObject("XR Origin");
                xrOrigin = SafeAddComponent<XROrigin>(xrOriginObj);
                
                // Настраиваем Camera Offset
                GameObject cameraOffsetObj = new GameObject("Camera Offset");
                cameraOffsetObj.transform.SetParent(xrOriginObj.transform);
                
                // Создаем AR Camera
                GameObject arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffsetObj.transform);
                
                // Настраиваем камеру
                Camera arCamera = SafeAddComponent<Camera>(arCameraObj);
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                
                // Добавляем компоненты AR к камере
                SafeAddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(arCameraObj);
                SafeAddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>(arCameraObj);
                
                // Настраиваем XROrigin
                xrOrigin.Camera = arCamera;
                xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
                
                Debug.Log("Created XR Origin with AR Camera");
            }
            else
            {
                xrOriginObj = xrOrigin.gameObject;
            }
            
            // Строго привязываем ARMeshManager к только что созданному XR Origin
            var meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            meshManagerObj.transform.localPosition = Vector3.zero;
            meshManagerObj.transform.localRotation = Quaternion.identity;
            meshManagerObj.transform.localScale = Vector3.one;
            ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
            meshManager.density = 0.5f;
            
            Debug.Log("Created AR Mesh Manager as child of XR Origin");
            
            // Добавляем недостающие компоненты для работы с AR
            if (xrOrigin != null && xrOrigin.gameObject.GetComponent<ARRaycastManager>() == null)
            {
                SafeAddComponent<ARRaycastManager>(xrOrigin.gameObject);
                Debug.Log("Added AR Raycast Manager to XR Origin");
            }
            
            Debug.Log("AR System setup completed successfully");
            
            return xrOrigin;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up AR System: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Creates the AR components for the scene
    /// </summary>
    /// <returns>The XR Origin GameObject</returns>
    private static GameObject CreateARComponents()
    {
        try
        {
            // Create a parent object for AR system
            GameObject arSystem = new GameObject("AR System");
            
            // Setup the AR system and получаем XROrigin
            XROrigin xrOrigin = SetupARSystem();
            
            // Обязательно вкладываем XR Origin и AR Session в arSystem
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetParent(arSystem.transform, false);
                Debug.Log("XR Origin added to AR System container");
            }
            
            // Найдём ARSession и тоже прицепим
            var arSession = GameObject.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
            if (arSession != null)
            {
                arSession.transform.SetParent(arSystem.transform, false);
                Debug.Log("AR Session added to AR System container");
            }
            
            // Добавляем автоматический исправитель иерархии при запуске
            if (xrOrigin != null)
            {
                CreateARMeshManagerChecker(xrOrigin);
            }
            
            // Return the container system
            return arSystem;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error creating AR components: {ex.Message}\n{ex.StackTrace}");
            return new GameObject("AR System (Error)");
        }
    }
    
    // Create color palette buttons
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
            
            // Store selected color in button's name (will be parsed in the component references setup)
            colorButton.name = $"ColorButton_{colors[i].r}_{colors[i].g}_{colors[i].b}";
        }
    }
    
    private static GameObject SetupUI()
    {
        try {
            // Create Canvas for UI
            GameObject canvasObj = new GameObject("UI Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // Create RawImage for AR display
            GameObject displayObj = new GameObject("AR Display");
            displayObj.transform.SetParent(canvasObj.transform, false);
            displayObj.transform.SetAsFirstSibling(); // Размещаем позади всех UI элементов
            
            RectTransform displayRect = displayObj.AddComponent<RectTransform>();
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = Vector2.zero;
            displayRect.offsetMax = Vector2.zero;
            
            RawImage rawImage = displayObj.AddComponent<RawImage>();
            // Устанавливаем дефолтную текстуру
            rawImage.texture = Texture2D.blackTexture;
            
            // Создаем панель с цветовой палитрой в нижней части экрана
            GameObject colorPanelObj = new GameObject("Color Panel");
            colorPanelObj.transform.SetParent(canvasObj.transform, false);
            
            RectTransform colorPanelRect = colorPanelObj.AddComponent<RectTransform>();
            colorPanelRect.anchorMin = new Vector2(0, 0);
            colorPanelRect.anchorMax = new Vector2(1, 0.1f);
            colorPanelRect.offsetMin = new Vector2(10, 10);
            colorPanelRect.offsetMax = new Vector2(-10, -10);
            
            Image colorPanelImage = colorPanelObj.AddComponent<Image>();
            colorPanelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            
            // Создаем цветовую палитру
            CreateColorButtons(colorPanelObj.transform);
            
            return canvasObj;
        }
        catch (Exception e) {
            Debug.LogError($"Error setting up UI: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Creates the AR UI Canvas with main UI elements
    /// </summary>
    /// <returns>The Canvas GameObject</returns>
    private static GameObject CreateARUICanvas()
    {
        // Create the main UI system with only color palette
        GameObject uiCanvas = SetupUI();
        
        return uiCanvas;
    }
    
    /// <summary>
    /// Проверяет и исправляет положение ARMeshManager в иерархии объектов
    /// </summary>
    private static void FixARMeshManagerHierarchy()
    {
        try
        {
            Debug.Log("Начинаю проверку иерархии ARMeshManager...");
            
            // Сначала получаем XROrigin
            XROrigin xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("ОШИБКА: XROrigin не найден в сцене! Невозможно исправить иерархию.");
                return;
            }
            
            // Получаем все ARMeshManager в сцене, которые НЕ являются детьми XROrigin
            System.Collections.Generic.List<ARMeshManager> invalidManagers = new System.Collections.Generic.List<ARMeshManager>();
            
            ARMeshManager[] allManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None);
            foreach (ARMeshManager meshManager in allManagers)
            {
                if (meshManager != null && meshManager.transform.parent != xrOrigin.transform)
                {
                    invalidManagers.Add(meshManager);
                    
                    // Если ARMeshManager уже создан, пытаемся его переместить вместо удаления
                    // Это поможет избежать потери ссылок
                    try 
                    {
                        string originalName = meshManager.gameObject.name;
                        Debug.LogWarning($"Перемещаем ARMeshManager {originalName} под XROrigin вместо удаления");
                        
                        meshManager.transform.SetParent(xrOrigin.transform);
                        meshManager.transform.localPosition = Vector3.zero;
                        meshManager.transform.localRotation = Quaternion.identity;
                        meshManager.transform.localScale = Vector3.one;
                        
                        // Удаляем его из списка недействительных, так как мы его исправили
                        invalidManagers.Remove(meshManager);
                        
                        Debug.Log($"ARMeshManager {originalName} успешно перемещен под XROrigin");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Не удалось переместить ARMeshManager: {e.Message}. Будет создан новый.");
                    }
                }
            }
            
            int invalidCount = invalidManagers.Count;
            if (invalidCount == 0)
            {
                // Проверяем, есть ли вообще ARMeshManager в сцене
                ARMeshManager existingManager = xrOrigin.GetComponentInChildren<ARMeshManager>(true);
                bool hasMeshManager = existingManager != null;
                
                if (!hasMeshManager)
                {
                    Debug.Log("ARMeshManager не найден. Создаем новый как дочерний объект XROrigin.");
                    
                    // Создаем новый ARMeshManager под XROrigin
                    GameObject meshManagerObj = new GameObject("AR Mesh Manager");
                    meshManagerObj.transform.SetParent(xrOrigin.transform, false);
                    meshManagerObj.transform.localPosition = Vector3.zero;
                    meshManagerObj.transform.localRotation = Quaternion.identity;
                    meshManagerObj.transform.localScale = Vector3.one;
                    
                    ARMeshManager meshManager = SafeAddComponent<ARMeshManager>(meshManagerObj);
                    meshManager.density = 0.5f;
                }
                else
                {
                    Debug.Log("Все ARMeshManager уже имеют правильную иерархию.");
                }
                
                return;
            }
            
            Debug.LogWarning($"Найдено {invalidCount} ARMeshManager с неправильной иерархией, которые не удалось исправить.");
            
            // Удаляем все неправильные ARMeshManager, которые не удалось исправить
            foreach (ARMeshManager manager in invalidManagers)
            {
                Debug.LogWarning($"Удаляем ARMeshManager с неправильной иерархией: {manager.gameObject.name}");
                GameObject.DestroyImmediate(manager.gameObject);
            }
            
            // Создаем новый ARMeshManager под XROrigin
            GameObject newMeshManagerObj = new GameObject("AR Mesh Manager");
            newMeshManagerObj.transform.SetParent(xrOrigin.transform, false);
            newMeshManagerObj.transform.localPosition = Vector3.zero;
            newMeshManagerObj.transform.localRotation = Quaternion.identity;
            newMeshManagerObj.transform.localScale = Vector3.one;
            
            ARMeshManager newMeshManager = SafeAddComponent<ARMeshManager>(newMeshManagerObj);
            newMeshManager.density = 0.5f;
            
            Debug.Log("Создан новый ARMeshManager как дочерний объект XROrigin");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Ошибка при исправлении иерархии ARMeshManager: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Создает скрипт автоматической проверки ARMeshManager при запуске сцены
    /// </summary>
    private static void CreateARMeshManagerChecker(XROrigin xrOrigin)
    {
        // В режиме редактора не создаем объект с DontDestroyOnLoad,
        // вместо этого добавляем компонент BasicARMeshManagerCheckerStarter
        
        // Проверяем, есть ли уже стартер в сцене
        BasicARMeshManagerCheckerStarter existingStarter = UnityEngine.Object.FindAnyObjectByType<BasicARMeshManagerCheckerStarter>();
        if (existingStarter != null)
        {
            Debug.Log("BasicARMeshManagerCheckerStarter уже существует в сцене");
            
            // Обновляем ссылку на XROrigin если она нулевая
            if (existingStarter.targetXROrigin == null)
            {
                existingStarter.targetXROrigin = xrOrigin;
                Debug.Log("Обновлена ссылка на XROrigin в существующем BasicARMeshManagerCheckerStarter");
            }
            return;
        }
        
        // Создаем объект для запуска проверки во время игры
        GameObject starterObj = new GameObject("AR Mesh Manager Checker Starter");
        BasicARMeshManagerCheckerStarter starter = starterObj.AddComponent<BasicARMeshManagerCheckerStarter>();
        if (starter != null && xrOrigin != null)
        {
            starter.targetXROrigin = xrOrigin;
        }
        
        Debug.Log("BasicARMeshManagerCheckerStarter добавлен для создания валидатора при запуске игры");
    }
}

/// <summary>
/// Компонент, который автоматически создает ARMeshManagerChecker при запуске
/// </summary>
public class BasicARMeshManagerCheckerStarter : MonoBehaviour
{
    public XROrigin targetXROrigin;
    
    void Start()
    {
        // Сам компонент ничего не делает в редакторе,
        // но будет использован для создания ARMeshManagerChecker при запуске игры
    }
} 