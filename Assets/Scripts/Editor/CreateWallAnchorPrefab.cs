using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEditor.SceneManagement;
using System.Reflection;
using UnityEngine.XR.ARFoundation;
using System.Linq;

public class CreateWallAnchorPrefab : EditorWindow
{
    [MenuItem("Tools/Wall Detection/Create Wall Anchor Template")]
    public static void CreatePrefab()
    {
        // Создаем GameObject с ARWallAnchor компонентом
        GameObject wallAnchorObj = new GameObject("Wall Anchor Template");
        wallAnchorObj.AddComponent<ARWallAnchor>();
        
        // Проверяем и создаем директорию если ее нет
        string prefabDirectory = "Assets/Prefabs/AR";
        if (!Directory.Exists(prefabDirectory))
        {
            Directory.CreateDirectory(prefabDirectory);
            AssetDatabase.Refresh();
        }
        
        // Путь к префабу
        string prefabPath = prefabDirectory + "/Wall Anchor Template.prefab";
        
        // Создаем и сохраняем префаб
        bool success = false;
        PrefabUtility.SaveAsPrefabAsset(wallAnchorObj, prefabPath, out success);
        Object.DestroyImmediate(wallAnchorObj);
        
        if (success)
        {
            Debug.Log($"[REMALUX] Префаб Wall Anchor Template успешно создан по пути: {prefabPath}");
            
            // Выделим префаб в Project view
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefabAsset;
            EditorGUIUtility.PingObject(prefabAsset);
        }
        else
        {
            Debug.LogError("[REMALUX] Не удалось создать префаб Wall Anchor Template!");
        }
    }
    
    [MenuItem("Tools/Wall Detection/Assign Wall Anchor Template")]
    public static void AssignWallAnchorTemplate()
    {
        // Находим RemaluxARWallSetup в сцене
        var wallSetup = Object.FindObjectOfType<RemaluxARWallSetup>();
        if (wallSetup == null)
        {
            Debug.LogError("[REMALUX] RemaluxARWallSetup не найден в сцене!");
            return;
        }
        
        // Путь к префабу
        string prefabPath = "Assets/Prefabs/AR/Wall Anchor Template.prefab";
        
        // Загружаем префаб
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[REMALUX] Префаб не найден по пути: {prefabPath}. Сначала создайте префаб с помощью 'Create Wall Anchor Template'.");
            return;
        }
        
        // Получаем SerializedObject для редактирования полей
        SerializedObject serializedObject = new SerializedObject(wallSetup);
        SerializedProperty wallAnchorPrefabProperty = serializedObject.FindProperty("_wallAnchorPrefab");
        
        // Устанавливаем значение
        wallAnchorPrefabProperty.objectReferenceValue = prefab;
        
        // Применяем изменения
        serializedObject.ApplyModifiedProperties();
        
        Debug.Log($"[REMALUX] Префаб Wall Anchor Template успешно назначен в RemaluxARWallSetup!");
    }
    
    [MenuItem("Tools/Wall Detection/Setup ARAnchorManager")]
    public static void SetupARAnchorManager()
    {
        // Находим ARSessionOrigin или XROrigin
        Component xrOrigin = null;
        
        // Вариант 1: Проверяем ARSessionOrigin (для обратной совместимости)
        var arSessionOrigins = Object.FindObjectsOfType<ARSessionOrigin>();
        if (arSessionOrigins.Length > 0)
        {
            xrOrigin = arSessionOrigins[0];
            Debug.Log($"[REMALUX] Найден ARSessionOrigin: {xrOrigin.name}");
        }
        else
        {
            // Вариант 2: Ищем XROrigin через рефлексию (для AR Foundation >= 5.0)
            try
            {
                var xrOriginType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.FullName == "Unity.XR.CoreUtils.XROrigin" || 
                                        t.FullName == "UnityEngine.XR.ARFoundation.XROrigin" ||
                                        t.FullName.EndsWith(".XROrigin"));
                                        
                if (xrOriginType != null)
                {
                    var xrOrigins = Object.FindObjectsOfType(xrOriginType);
                    
                    if (xrOrigins.Length > 0)
                    {
                        xrOrigin = xrOrigins[0] as Component;
                        Debug.Log($"[REMALUX] Найден XROrigin: {xrOrigin.name}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[REMALUX] Ошибка при поиске XROrigin: {ex.Message}");
            }
        }
        
        if (xrOrigin == null)
        {
            Debug.LogError("[REMALUX] Не удалось найти ARSessionOrigin или XROrigin в сцене!");
            return;
        }
        
        // Проверяем наличие префаба Wall Anchor Template
        string prefabPath = "Assets/Prefabs/AR/Wall Anchor Template.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (prefab == null)
        {
            Debug.LogError($"[REMALUX] Префаб не найден по пути: {prefabPath}. Сначала создайте префаб с помощью 'Create Wall Anchor Template'.");
            return;
        }
        
        // Проверяем/добавляем ARAnchorManager
        ARAnchorManager anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
        bool wasAdded = false;
        
        if (anchorManager == null)
        {
            anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();
            wasAdded = true;
            Debug.Log($"[REMALUX] Добавлен компонент ARAnchorManager к {xrOrigin.name}");
        }
        else
        {
            Debug.Log($"[REMALUX] Найден существующий ARAnchorManager на {xrOrigin.name}");
        }
        
        // Настраиваем ARAnchorManager - назначаем префаб
        SerializedObject serializedAnchorManager = new SerializedObject(anchorManager);
        SerializedProperty anchorPrefabProperty = serializedAnchorManager.FindProperty("m_AnchorPrefab");
        
        if (anchorPrefabProperty != null)
        {
            anchorPrefabProperty.objectReferenceValue = prefab;
            serializedAnchorManager.ApplyModifiedProperties();
            Debug.Log($"[REMALUX] Префаб Wall Anchor Template успешно назначен в ARAnchorManager");
        }
        else
        {
            Debug.LogWarning("[REMALUX] Не удалось найти свойство m_AnchorPrefab в ARAnchorManager");
        }
        
        // Обновляем RemaluxARWallSetup
        var wallSetup = Object.FindObjectOfType<RemaluxARWallSetup>();
        if (wallSetup != null)
        {
            SerializedObject serializedWallSetup = new SerializedObject(wallSetup);
            
            // Назначаем ARAnchorManager
            SerializedProperty arAnchorManagerProperty = serializedWallSetup.FindProperty("_arAnchorManager");
            if (arAnchorManagerProperty != null)
            {
                arAnchorManagerProperty.objectReferenceValue = anchorManager;
                serializedWallSetup.ApplyModifiedProperties();
                Debug.Log($"[REMALUX] ARAnchorManager успешно назначен в RemaluxARWallSetup");
            }
            else
            {
                Debug.LogWarning("[REMALUX] Не удалось найти свойство _arAnchorManager в RemaluxARWallSetup");
            }
            
            // Также назначим Wall Anchor Prefab
            SerializedProperty wallAnchorPrefabProperty = serializedWallSetup.FindProperty("_wallAnchorPrefab");
            if (wallAnchorPrefabProperty != null)
            {
                wallAnchorPrefabProperty.objectReferenceValue = prefab;
                serializedWallSetup.ApplyModifiedProperties();
                Debug.Log($"[REMALUX] Префаб Wall Anchor Template также назначен в RemaluxARWallSetup");
            }
        }
        else
        {
            Debug.LogWarning("[REMALUX] RemaluxARWallSetup не найден в сцене!");
        }
        
        if (wasAdded)
        {
            Debug.Log("[REMALUX] ARAnchorManager успешно настроен и добавлен к XROrigin. Необходимо сохранить сцену!");
        }
        else
        {
            Debug.Log("[REMALUX] ARAnchorManager успешно настроен. Необходимо сохранить сцену!");
        }
    }

    [MenuItem("Tools/Wall Detection/Create Complete AR Scene")]
    public static void CreateARScene()
    {
        // Спрашиваем пользователя, хочет ли он сохранить текущую сцену
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("Операция отменена пользователем");
                return;
            }
        }

        // Создаем новую пустую сцену
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // 1. Создаем AR Scene корневой объект
        GameObject arScene = new GameObject("ARScene");
        
        // 2. Создаем Directional Light
        GameObject lightObj = new GameObject("Directional Light");
        lightObj.transform.SetParent(arScene.transform);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = Color.white;
        lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        // 3. Создаем AR System
        GameObject arSystem = new GameObject("AR System");
        arSystem.transform.SetParent(arScene.transform);
        
        // 4. Создаем AR Session
        GameObject arSession = new GameObject("AR Session");
        arSession.transform.SetParent(arSystem.transform);
        arSession.AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
        arSession.AddComponent<UnityEngine.XR.ARFoundation.ARInputManager>();
        
        // Получаем Unity XR Origin тип через рефлексию для совместимости с разными версиями
        System.Type xrOriginType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
        GameObject xrOrigin;
        
        // 5. Создаем XR Origin или ARSessionOrigin в зависимости от доступности типа
        if (xrOriginType != null)
        {
            xrOrigin = new GameObject("XR Origin");
            xrOrigin.transform.SetParent(arSystem.transform);
            xrOrigin.AddComponent(xrOriginType);
            
            // Добавляем также ARSessionOrigin для совместимости со старыми скриптами
            xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARSessionOrigin>();
            
            // Создаем Camera Offset
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);
            
            // Создаем AR Camera
            GameObject arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(cameraOffset.transform);
            Camera camera = arCamera.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            
            // Добавляем компоненты трекинга на камеру
            arCamera.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
            arCamera.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            
            // Настраиваем XROrigin
            var xrOriginComponent = xrOrigin.GetComponent(xrOriginType);
            var cameraProperty = xrOriginType.GetProperty("Camera");
            if (cameraProperty != null)
            {
                cameraProperty.SetValue(xrOriginComponent, camera);
            }
            
            // Настраиваем ARSessionOrigin
            var sessionOrigin = xrOrigin.GetComponent<UnityEngine.XR.ARFoundation.ARSessionOrigin>();
            sessionOrigin.camera = camera;
        }
        else
        {
            // Создаем классический ARSessionOrigin для старых версий
            xrOrigin = new GameObject("ARSessionOrigin");
            xrOrigin.transform.SetParent(arSystem.transform);
            var sessionOrigin = xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARSessionOrigin>();
            
            // Создаем AR Camera
            GameObject arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(xrOrigin.transform);
            Camera camera = arCamera.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            
            // Добавляем компоненты трекинга на камеру
            arCamera.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
            arCamera.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            
            // Настраиваем ARSessionOrigin
            sessionOrigin.camera = camera;
        }
        
        // 6. Добавляем AR компоненты на XR Origin
        xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARRaycastManager>();
        var anchorManager = xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>();
        xrOrigin.AddComponent<UnityEngine.XR.ARFoundation.ARMeshManager>();
        
        // 7. Создаем ML System
        GameObject mlSystem = new GameObject("ML System");
        mlSystem.transform.SetParent(arSystem.transform);
        
        // 8. Создаем пустые объекты для ML компонентов
        // Заменяем на MonoBehaviour и GameObject.AddComponent вместо конкретных типов
        GameObject deepLabObj = new GameObject("DeepLab Predictor");
        deepLabObj.transform.SetParent(mlSystem.transform);
        // Не добавляем компонент ML.DeepLab.DeepLabPredictor, если он не существует
        
        GameObject enhancedDeepLabObj = new GameObject("Enhanced DeepLab Predictor");
        enhancedDeepLabObj.transform.SetParent(mlSystem.transform);
        // Пытаемся динамически найти и добавить компонент
        System.Type enhancedType = System.Type.GetType("ML.DeepLab.EnhancedDeepLabPredictor, Assembly-CSharp");
        MonoBehaviour enhancedPredictor = null;
        if (enhancedType != null)
        {
            enhancedPredictor = enhancedDeepLabObj.AddComponent(enhancedType) as MonoBehaviour;
            
            // Если тип найден, пытаемся настроить его через рефлексию
            if (enhancedPredictor != null)
            {
                try {
                    enhancedType.GetField("allowFallbackShader")?.SetValue(enhancedPredictor, true);
                    enhancedType.GetField("applyNoiseReduction")?.SetValue(enhancedPredictor, true);
                    enhancedType.GetField("applyWallFilling")?.SetValue(enhancedPredictor, true);
                    enhancedType.GetField("applyTemporalSmoothing")?.SetValue(enhancedPredictor, true);
                    enhancedType.GetField("useArgMaxMode")?.SetValue(enhancedPredictor, true);
                    enhancedType.GetField("debugMode")?.SetValue(enhancedPredictor, true);
                } catch (System.Exception e) {
                    Debug.LogWarning($"Не удалось настроить EnhancedDeepLabPredictor: {e.Message}");
                }
            }
        }
        
        // 9. Создаем ML Manager
        GameObject mlManagerObj = new GameObject("ML Manager");
        mlManagerObj.transform.SetParent(mlSystem.transform);
        // Не добавляем ML.MLManager
        
        // 10. Создаем Wall Detection System
        GameObject wallDetectionObj = new GameObject("Wall Detection System");
        wallDetectionObj.transform.SetParent(mlSystem.transform);
        // Пытаемся найти тип через рефлексию
        System.Type wallDetectionType = System.Type.GetType("RemaluxWallDetectionSystem, Assembly-CSharp");
        MonoBehaviour wallDetectionSystem = null;
        if (wallDetectionType != null)
        {
            wallDetectionSystem = wallDetectionObj.AddComponent(wallDetectionType) as MonoBehaviour;
        }
        else
        {
            // Если рефлексия не сработала, попробуем через FindObjectOfType
            wallDetectionObj.AddComponent<MonoBehaviour>();
            Debug.LogWarning("RemaluxWallDetectionSystem тип не найден, создан пустой объект");
        }
        
        // 11. Создаем Remalux AR Wall System
        GameObject remaluxObj = new GameObject("Remalux AR Wall System");
        remaluxObj.transform.SetParent(mlSystem.transform);
        // Пытаемся найти тип через рефлексию
        System.Type remaluxType = System.Type.GetType("RemaluxARWallSetup, Assembly-CSharp");
        MonoBehaviour remaluxSetup = null;
        if (remaluxType != null)
        {
            remaluxSetup = remaluxObj.AddComponent(remaluxType) as MonoBehaviour;
        }
        else
        {
            // Если рефлексия не сработала, попробуем через FindObjectOfType
            remaluxObj.AddComponent<MonoBehaviour>();
            Debug.LogWarning("RemaluxARWallSetup тип не найден, создан пустой объект");
        }
        
        // 12. Создаем Wall Colorizer
        GameObject wallColorizerObj = new GameObject("Wall Colorizer");
        wallColorizerObj.transform.SetParent(mlSystem.transform);
        // Пытаемся найти тип через рефлексию
        System.Type wallColorizerType = System.Type.GetType("WallColorizer, Assembly-CSharp");
        if (wallColorizerType != null)
        {
            wallColorizerObj.AddComponent(wallColorizerType);
        }
        else
        {
            wallColorizerObj.AddComponent<MonoBehaviour>();
            Debug.LogWarning("WallColorizer тип не найден, создан пустой объект");
        }
        
        // 13. Создаем ARML Controller
        GameObject armlControllerObj = new GameObject("ARML Controller");
        armlControllerObj.transform.SetParent(mlSystem.transform);
        
        // 14. Создаем Wall System
        GameObject wallSystemObj = new GameObject("Wall System");
        wallSystemObj.transform.SetParent(arSystem.transform);
        
        // 15. Создаем UI Canvas
        GameObject uiCanvasObj = new GameObject("UI Canvas");
        uiCanvasObj.transform.SetParent(arScene.transform);
        var canvas = uiCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiCanvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        uiCanvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        
        // Создаем AR Display
        GameObject arDisplayObj = new GameObject("AR Display");
        arDisplayObj.transform.SetParent(uiCanvasObj.transform);
        arDisplayObj.AddComponent<UnityEngine.UI.RawImage>();
        
        // Создаем Color Panel
        GameObject colorPanelObj = new GameObject("Color Panel");
        colorPanelObj.transform.SetParent(uiCanvasObj.transform);
        
        // 16. Создаем или загружаем префаб Wall Anchor Template
        CreatePrefab();
        
        // Загружаем созданный префаб
        string prefabPath = "Assets/Prefabs/AR/Wall Anchor Template.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (prefab != null)
        {
            // Назначаем префаб в ARAnchorManager
            SerializedObject anchorManagerSerializedObj = new SerializedObject(anchorManager);
            SerializedProperty prefabPropertyForAM = anchorManagerSerializedObj.FindProperty("m_AnchorPrefab");
            
            if (prefabPropertyForAM != null)
            {
                prefabPropertyForAM.objectReferenceValue = prefab;
                anchorManagerSerializedObj.ApplyModifiedProperties();
                
                Debug.Log("Префаб Wall Anchor Template назначен в ARAnchorManager");
            }
            
            // Если системы созданы успешно через рефлексию, пытаемся настроить их
            if (remaluxSetup != null && remaluxType != null)
            {
                // Используем рефлексию для настройки
                try {
                    SerializedObject serializedRemalux = new SerializedObject(remaluxSetup);
                    SerializedProperty wallPrefabProperty = serializedRemalux.FindProperty("_wallAnchorPrefab");
                    SerializedProperty arAnchorManagerProperty = serializedRemalux.FindProperty("_arAnchorManager");
                    
                    if (wallPrefabProperty != null && arAnchorManagerProperty != null)
                    {
                        wallPrefabProperty.objectReferenceValue = prefab;
                        arAnchorManagerProperty.objectReferenceValue = anchorManager;
                        serializedRemalux.ApplyModifiedProperties();
                        
                        Debug.Log("Префаб и ARAnchorManager назначены в RemaluxARWallSetup");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning($"Не удалось настроить RemaluxARWallSetup: {e.Message}");
                }
            }
            
            // Если система создана успешно через рефлексию, пытаемся настроить её
            if (wallDetectionSystem != null && wallDetectionType != null)
            {
                try {
                    SerializedObject serializedWallDetection = new SerializedObject(wallDetectionSystem);
                    SerializedProperty wdWallPrefabProperty = serializedWallDetection.FindProperty("_wallAnchorPrefab");
                    SerializedProperty wdAnchorManagerProperty = serializedWallDetection.FindProperty("_arAnchorManager");
                    
                    if (wdWallPrefabProperty != null && wdAnchorManagerProperty != null)
                    {
                        wdWallPrefabProperty.objectReferenceValue = prefab;
                        wdAnchorManagerProperty.objectReferenceValue = anchorManager;
                        serializedWallDetection.ApplyModifiedProperties();
                        
                        Debug.Log("Префаб и ARAnchorManager назначены в RemaluxWallDetectionSystem");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning($"Не удалось настроить RemaluxWallDetectionSystem: {e.Message}");
                }
            }
        }
        
        // Сохраняем сцену
        string scenePath = "Assets/Scenes/ARScene.unity";
        
        // Проверяем, существует ли директория
        string sceneDirectory = Path.GetDirectoryName(scenePath);
        if (!Directory.Exists(sceneDirectory)) 
        {
            Directory.CreateDirectory(sceneDirectory);
        }
        
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath, false);
        
        Debug.Log($"Полная AR сцена создана и сохранена в {scenePath}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
    }
} 