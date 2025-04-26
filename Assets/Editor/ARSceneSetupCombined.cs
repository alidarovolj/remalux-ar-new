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
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SpatialTracking;
using System.Reflection;
using Unity.Barracuda;
using ML.DeepLab;

/// <summary>
/// Комплексная настройка AR + ML сцены в один шаг
/// </summary>
public class ARSceneSetupAllInOne : EditorWindow
{
    private bool setupAR = true;
    private bool setupML = true;
    private bool addDebugViewer = true;
    private bool ensureCorrectHierarchy = true;
    private bool useCameraSpaceCanvas = true;
    private bool fixARCamera = true;
    
    private string configStatus = "";
    private MessageType statusType = MessageType.Info;

    private Vector2 scrollPosition = Vector2.zero;
    private GUIStyle centeredTextStyle;

    [MenuItem("AR/Setup AR Scene (Complete)", priority = 0)]
    public static void ShowWindow()
    {
        ARSceneSetupAllInOne window = GetWindow<ARSceneSetupAllInOne>("AR Scene Setup");
        window.minSize = new Vector2(400, 300);
    }

    private void OnEnable()
    {
        // Don't initialize styles here, as EditorStyles might not be ready yet
    }
    
    // Add a method to ensure styles are initialized when needed
    private void EnsureStylesInitialized()
    {
        if (centeredTextStyle == null)
        {
            centeredTextStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
        }
    }

    private void OnGUI()
    {
        // Initialize styles when actually needed
        EnsureStylesInitialized();
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("AR Scene Setup (All-in-one)", centeredTextStyle);
        EditorGUILayout.Space(10);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Основные компоненты", EditorStyles.boldLabel);
        SetupARSystem();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Дополнительные компоненты", EditorStyles.boldLabel);
        SetupARSystemManager();
        SetupMLSystem();
        CreateARPlaneVisualizer();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Автоматизация и исправления", EditorStyles.boldLabel);
        AddARRuntimeFixer();
        EditorGUILayout.EndVertical();
        
        if (!string.IsNullOrEmpty(configStatus))
        {
            EditorGUILayout.HelpBox(configStatus, statusType);
            EditorGUILayout.Space(10);
        }
        
        EditorGUILayout.Space(20);
        
        EditorGUILayout.LabelField("Настройка визуализации AR", centeredTextStyle);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Добавить визуализатор AR-плоскостей", GUILayout.Height(30)))
        {
            CreateARPlaneVisualizer();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.HelpBox("Это добавит визуализатор для AR-плоскостей, который позволит видеть обнаруженные плоскости в приложении.", MessageType.Info);
        
        EditorGUILayout.Space(20);
        
        if (GUILayout.Button("Настроить сцену", GUILayout.Height(40)))
        {
            RunCompleteSetup();
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void RunCompleteSetup()
    {
        try
        {
            // Save current scene if it has unsaved changes
            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (EditorUtility.DisplayDialog("Save Current Scene", 
                    "Do you want to save the current scene before setting up AR?", "Save", "Continue Without Saving"))
                {
                    EditorSceneManager.SaveScene(currentScene);
                }
            }
            
            configStatus = "Starting setup process...";
            statusType = MessageType.Info;
            
            // Важно: изменим порядок операций для большей надежности
            
            // 1. Сначала удаляем дублирующиеся ARSession, чтобы избежать конфликтов
            CleanupDuplicateARSessions();
            
            // 2. Установка базовой AR системы
            if (setupAR)
            {
                SetupARSystem();
                configStatus = "AR System setup complete.";
            }
            
            // 3. Сразу же исправляем ссылку на камеру - это критически важно
            XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                FixXROriginCameraReference(xrOrigin);
                configStatus = configStatus + "\nXR Origin camera reference fixed.";
            }
            
            // 4. Проверяем и исправляем ARCamera
            if (fixARCamera)
            {
                FixARCamera();
                configStatus = configStatus + "\nAR Camera fixed.";
            }
            
            // 5. Затем проверяем иерархию компонентов
            if (ensureCorrectHierarchy)
            {
                EnsureCorrectHierarchy();
                configStatus = configStatus + "\nComponent hierarchy fixed.";
            }
            
            // 6. Повторно проверяем ARSession
            CleanupDuplicateARSessions();
            
            // 7. Настраиваем ML после того, как AR система готова
            if (setupML)
            {
                SetupMLSystem();
                configStatus = configStatus + "\nML System setup complete.";
            }
            
            // 8. Добавляем отладочный вьювер
            if (addDebugViewer)
            {
                AddMLDebugViewer();
                configStatus = configStatus + "\nML Debug Viewer added.";
            }
            
            // 9. Исправляем ссылки в ARMLController
            if (setupML)
            {
                FixARMLControllerReferences();
                configStatus = configStatus + "\nAR ML Controller references fixed.";
            }
            
            // 10. Финальная проверка иерархии и ссылок
            if (setupAR)
            {
                // Проверяем еще раз ссылку на камеру
                xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null)
                {
                    FixXROriginCameraReference(xrOrigin);
                }
                
                // Финальная проверка дубликатов ARSession
                CleanupDuplicateARSessions();
            }
            
            // 11. Добавляем ARRuntimeFixer для исправления проблем во время выполнения
            AddARRuntimeFixer();
            
            // 12. Финальная проверка всех ARSessions перед завершением
            CleanupDuplicateARSessions();
            
            // Mark scene as dirty to ensure changes are saved
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            
            configStatus = configStatus + "\n\nAR+ML Setup completed successfully!";
            statusType = MessageType.Info;
            
            // Select the AR System in hierarchy
            GameObject arSystem = GameObject.Find("AR System");
            if (arSystem != null)
            {
                Selection.activeGameObject = arSystem;
            }
            
            Debug.Log("AR+ML Setup completed successfully!");
        }
        catch (Exception ex)
        {
            configStatus = $"Error during setup: {ex.Message}";
            statusType = MessageType.Error;
            Debug.LogError($"Error during AR+ML setup: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    private void CleanupDuplicateARSessions()
    {
        // Find all ARSessions in the scene
        ARSession[] arSessions = UnityEngine.Object.FindObjectsByType<ARSession>(FindObjectsSortMode.None);
        int sessionCount = arSessions.Length;
        
        // If there's only one or none, nothing to do
        if (sessionCount <= 1)
        {
            Debug.Log("No duplicate ARSessions found.");
            return;
        }
        
        Debug.LogWarning($"Found {sessionCount} ARSessions, removing duplicates");
        
        // Keep track of which session we'll keep (prefer one that's under AR System)
        ARSession sessionToKeep = null;
        GameObject arSystem = GameObject.Find("AR System");
        
        if (arSystem != null)
        {
            // First look for ARSession that's a direct child of AR System
            foreach (ARSession session in arSessions)
            {
                if (session.transform.parent == arSystem.transform)
                {
                    sessionToKeep = session;
                    break;
                }
            }
        }
        
        // If no session found under AR System, keep the first one
        if (sessionToKeep == null && arSessions.Length > 0)
        {
            sessionToKeep = arSessions[0];
        }
        
        // Disable or destroy duplicates
        foreach (ARSession session in arSessions)
        {
            if (session != sessionToKeep)
            {
                // Instead of destroying, sometimes it's safer to just disable
                if (session.gameObject.name == "AR System")
                {
                    // This is the AR System itself, so just disable the component
                    Debug.Log($"Disabling duplicate ARSession on {session.gameObject.name}");
                    session.enabled = false;
                }
                else if (session.gameObject.GetComponents<Component>().Length <= 2) // Only Transform and ARSession
                {
                    // The GameObject has no other important components, safe to destroy
                    Debug.Log($"Destroying duplicate ARSession GameObject: {session.gameObject.name}");
                    GameObject.DestroyImmediate(session.gameObject);
                }
                else
                {
                    // GameObject has other components, so just disable the ARSession
                    Debug.Log($"Disabling duplicate ARSession on {session.gameObject.name}");
                    session.enabled = false;
                }
            }
        }
        
        // Ensure the remaining session is enabled and properly set up
        if (sessionToKeep != null)
        {
            sessionToKeep.enabled = true;
            
            // If this session isn't under AR System, move it there
            if (arSystem != null && sessionToKeep.transform.parent != arSystem.transform)
            {
                sessionToKeep.transform.SetParent(arSystem.transform, true);
                Debug.Log($"Moved ARSession under AR System");
            }
            
            Debug.Log($"Keeping ARSession on {sessionToKeep.gameObject.name}");
        }
    }
    
    private void FixARMLControllerReferences()
    {
        Debug.Log("Fixing ARMLController references");
        
        // Find the ML System (assuming it already exists)
        GameObject mlSystem = GameObject.Find("ML System");
        if (mlSystem == null)
        {
            Debug.LogWarning("ML System not found, creating it");
            mlSystem = new GameObject("ML System");
        }
        
        // Find the ARMLController
        ARMLController armlController = UnityEngine.Object.FindFirstObjectByType<ARMLController>();
        if (armlController == null)
        {
            Debug.LogWarning("ARMLController not found, cannot fix references");
            return;
        }
        
        // Find SegmentationManager (don't create if not found)
        SegmentationManager segmentationManager = UnityEngine.Object.FindFirstObjectByType<SegmentationManager>();
        if (segmentationManager == null)
        {
            Debug.LogWarning("SegmentationManager not found, cannot fix ARMLController references");
            return;
        }
        
        // Find the MaskProcessor component
        MaskProcessor maskProcessor = UnityEngine.Object.FindFirstObjectByType<MaskProcessor>();
        
        if (maskProcessor == null)
        {
            GameObject maskProcObj = new GameObject("MaskProcessor");
            maskProcObj.transform.SetParent(mlSystem.transform, false);
            maskProcessor = maskProcObj.AddComponent<MaskProcessor>();
            Debug.Log("Created new MaskProcessor");
        }
        
        // Find or create EnhancedDeepLabPredictor
        bool enhancedDeepLabExists = TryCreateEnhancedDeepLabPredictor(mlSystem, segmentationManager);
        
        // If we couldn't create EnhancedDeepLabPredictor, try to create a simple DeepLabPredictor
        if (!enhancedDeepLabExists)
        {
            TryCreateSimpleDeepLabPredictor(mlSystem, segmentationManager);
        }

        // Set up references with SerializedObject
        if (armlController != null)
        {
            SerializedObject serializedController = new SerializedObject(armlController);
            
            // Set segmentationManager
            SerializedProperty segManagerProp = serializedController.FindProperty("segmentationManager");
            if (segManagerProp != null && segmentationManager != null)
            {
                segManagerProp.objectReferenceValue = segmentationManager;
                Debug.Log("Set segmentationManager in ARMLController");
            }
            
            // Set maskProcessor
            SerializedProperty maskProcProp = serializedController.FindProperty("maskProcessor");
            if (maskProcProp != null && maskProcessor != null)
            {
                maskProcProp.objectReferenceValue = maskProcessor;
                Debug.Log("Set maskProcessor in ARMLController");
            }
            
            // Set cameraManager
            var xrOrigin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                var cameraManager = xrOrigin.Camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
                if (cameraManager != null)
                {
                    SerializedProperty camManagerProp = serializedController.FindProperty("cameraManager");
                    if (camManagerProp != null)
                    {
                        camManagerProp.objectReferenceValue = cameraManager;
                        Debug.Log("Set cameraManager in ARMLController");
                    }
                }
                else
                {
                    Debug.LogWarning("Could not find ARCameraManager on XROrigin camera");
                }
            }
            else
            {
                // Fix XROrigin Camera reference first
                if (xrOrigin != null)
                {
                    FixXROriginCameraReference(xrOrigin);
                    
                    // Try again with the fixed camera reference
                    if (xrOrigin.Camera != null)
                    {
                        var cameraManager = xrOrigin.Camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
                        if (cameraManager != null)
                        {
                            SerializedProperty camManagerProp = serializedController.FindProperty("cameraManager");
                            if (camManagerProp != null)
                            {
                                camManagerProp.objectReferenceValue = cameraManager;
                                Debug.Log("Set cameraManager in ARMLController after fixing XROrigin");
                            }
                        }
                    }
                }
            }
            
            // Set arSession
            var arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (arSession != null)
            {
                SerializedProperty sessionProp = serializedController.FindProperty("arSession");
                if (sessionProp != null)
                {
                    sessionProp.objectReferenceValue = arSession;
                    Debug.Log("Set arSession in ARMLController");
                }
            }
            
            // Apply all changes
            serializedController.ApplyModifiedProperties();
            
            // Set runtime variables via reflection to make sure they're properly initialized
            if (segmentationManager != null)
            {
                var segManagerField = armlController.GetType().GetField("segmentationManager", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (segManagerField != null)
                {
                    segManagerField.SetValue(armlController, segmentationManager);
                }
            }
            
            if (maskProcessor != null)
            {
                var maskProcField = armlController.GetType().GetField("maskProcessor", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (maskProcField != null)
                {
                    maskProcField.SetValue(armlController, maskProcessor);
                }
            }
            
            // Enable ARMLController
            armlController.enabled = true;
            Debug.Log("Enabled ARMLController");
        }
    }
    
    /// <summary>
    /// Находит модель model.onnx в проекте
    /// </summary>
    private NNModel FindModelAsset()
    {
        try
        {
            // Первым делом ищем конкретно model.onnx в известной локации
            string specificPath = "Assets/ML/Models/model.onnx";
            var modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<NNModel>(specificPath);
            
            if (modelAsset != null)
            {
                Debug.Log($"Found model.onnx at specific path: {specificPath}");
                return modelAsset;
            }
            
            // Если не нашли в конкретной папке, ищем глобально по имени
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:NNModel model");
            
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("model.onnx"))
                {
                    modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<NNModel>(path);
                    Debug.Log($"Found model.onnx at path: {path}");
                    return modelAsset;
                }
            }
            
            // Если не нашли точное совпадение, ищем любую .onnx модель
            if (modelAsset == null)
            {
                guids = UnityEditor.AssetDatabase.FindAssets("t:NNModel");
                
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<NNModel>(path);
                    Debug.LogWarning($"Could not find model.onnx. Using alternative model: {path}");
                    return modelAsset;
                }
            }
            
            Debug.LogError("Could not find any ONNX model in the project. Please ensure model.onnx is imported.");
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error finding model asset: {e.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Настраивает SegmentationManager, устанавливая правильную модель и параметры
    /// </summary>
    private void SetModelOnSegmentationManager(SegmentationManager segmentationManager, NNModel modelAsset)
    {
        if (segmentationManager == null || modelAsset == null)
            return;
            
        // Получаем доступ к полям компонента через SerializedObject
        var serializedObject = new UnityEditor.SerializedObject(segmentationManager);
        
        // Устанавливаем модель
        var modelProperty = serializedObject.FindProperty("modelAsset");
        if (modelProperty != null)
        {
            modelProperty.objectReferenceValue = modelAsset;
        }
        
        // Устанавливаем ID класса стен (по умолчанию 9 для ADE20K)
        // Для dataset ADE20K класс стен имеет ID = 9
        var wallClassIdProperty = serializedObject.FindProperty("wallClassId");
        if (wallClassIdProperty != null)
        {
            // Проверяем, используется ли ADE20K набор данных по имени модели
            bool isAde20k = modelAsset.name.ToLower().Contains("ade") || 
                            modelAsset.name.ToLower().Contains("deeplab");
                            
            // Устанавливаем соответствующий ID класса стен:
            // - ADE20K: 9
            // - COCO: 11
            // - Cityscapes: 2
            // По умолчанию используем ADE20K (9)
            byte wallId = isAde20k ? (byte)9 : (byte)9;
            wallClassIdProperty.intValue = wallId;
            
            Debug.Log($"Setting wall class ID to {wallId} based on model {modelAsset.name}");
        }
        
        // Устанавливаем порог для классификации
        var thresholdProperty = serializedObject.FindProperty("classificationThreshold");
        if (thresholdProperty != null)
        {
            thresholdProperty.floatValue = 0.5f;
        }
        
        // Применяем изменения
        serializedObject.ApplyModifiedProperties();
    }
    
    /// <summary>
    /// Создает и настраивает EnhancedDeepLabPredictor
    /// </summary>
    private bool TryCreateEnhancedDeepLabPredictor(GameObject mlSystemObj, SegmentationManager segmentationManager)
    {
        try
        {
            // Ищем существующий EnhancedDeepLabPredictor
            var existingPredictor = UnityEngine.Object.FindFirstObjectByType<EnhancedDeepLabPredictor>();
            if (existingPredictor != null)
            {
                // Если уже есть, просто устанавливаем ссылку на SegmentationManager
                if (segmentationManager != null)
                {
                    var serializedObject = new UnityEditor.SerializedObject(existingPredictor);
                    var segmentationManagerProp = serializedObject.FindProperty("segmentationManager");
                    
                    if (segmentationManagerProp != null)
                    {
                        segmentationManagerProp.objectReferenceValue = segmentationManager;
                        serializedObject.ApplyModifiedProperties();
                        Debug.Log("Set SegmentationManager reference on existing EnhancedDeepLabPredictor");
                    }
                }
                
                return true;
            }
            
            // Создаем объект для EnhancedDeepLabPredictor
            GameObject predictorObj = new GameObject("Enhanced DeepLab Predictor");
            predictorObj.transform.SetParent(mlSystemObj.transform, false);
            
            // Добавляем компонент EnhancedDeepLabPredictor
            var enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
            
            // Настраиваем EnhancedDeepLabPredictor
            UnityEditor.SerializedObject predictorSO = new UnityEditor.SerializedObject(enhancedPredictor);
            
            // Устанавливаем ссылку на SegmentationManager
            var predictorSegManagerProp = predictorSO.FindProperty("segmentationManager");
            if (predictorSegManagerProp != null && segmentationManager != null)
            {
                predictorSegManagerProp.objectReferenceValue = segmentationManager;
            }
            
            // Находим модель ONNX
            NNModel modelAsset = FindModelAsset();
            
            // Устанавливаем ссылку на модель
            var modelProperty = predictorSO.FindProperty("modelAsset");
            if (modelProperty != null && modelAsset != null)
            {
                modelProperty.objectReferenceValue = modelAsset;
            }
            
            // Устанавливаем ID класса стен
            var wallClassIdProperty = predictorSO.FindProperty("WallClassId");
            if (wallClassIdProperty != null)
            {
                // Если модель основана на ADE20K, используем ID 9, иначе настраиваем в зависимости от модели
                bool isAde20k = modelAsset != null && (modelAsset.name.ToLower().Contains("ade") || 
                                                       modelAsset.name.ToLower().Contains("deeplab"));
                int wallId = isAde20k ? 9 : 9; // По умолчанию используем ADE20K (9)
                wallClassIdProperty.intValue = wallId;
                
                Debug.Log($"Setting EnhancedDeepLabPredictor wall class ID to {wallId}");
            }
            
            // Устанавливаем порог для классификации
            var thresholdProperty = predictorSO.FindProperty("ClassificationThreshold");
            if (thresholdProperty != null)
            {
                thresholdProperty.floatValue = 0.5f;
                Debug.Log("Setting ClassificationThreshold to 0.5");
            }
            
            // Применяем все изменения
            predictorSO.ApplyModifiedProperties();
            
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating EnhancedDeepLabPredictor: {e.Message}");
            return false;
        }
    }
    
    private bool TryCreateSimpleDeepLabPredictor(GameObject mlSystemObj, SegmentationManager segmentationManager)
    {
        // Check if DeepLabPredictor exists
        Type predictorType = System.Type.GetType("ML.DeepLab.DeepLabPredictor, Assembly-CSharp");
        
        if (predictorType == null)
        {
            Debug.LogWarning("DeepLabPredictor type not found in assembly");
            return false;
        }
        
        // Check for existing instance
        Component existingPredictor = (Component)UnityEngine.Object.FindFirstObjectByType(predictorType);
        if (existingPredictor != null)
        {
            Debug.Log("Found existing DeepLabPredictor");
            
            // Connect to SegmentationManager if needed
            if (segmentationManager != null)
            {
                // Try to set segmentationManager field using reflection
                FieldInfo segManagerField = predictorType.GetField("segmentationManager", 
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    
                if (segManagerField != null)
                {
                    segManagerField.SetValue(existingPredictor, segmentationManager);
                    Debug.Log("Set SegmentationManager reference in existing DeepLabPredictor");
                }
            }
            
            return true;
        }
        
        // Create new DeepLabPredictor
        try
        {
            GameObject predictorObj = new GameObject("DeepLabPredictor");
            predictorObj.transform.SetParent(mlSystemObj.transform, false);
            
            Component predictor = predictorObj.AddComponent(predictorType);
            
            // Set SegmentationManager reference
            if (segmentationManager != null)
            {
                FieldInfo segManagerField = predictorType.GetField("segmentationManager", 
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    
                if (segManagerField != null)
                {
                    segManagerField.SetValue(predictor, segmentationManager);
                    Debug.Log("Set SegmentationManager reference in new DeepLabPredictor");
                }
            }
            
            // Look for model asset and set it
            NNModel modelAsset = FindModelAsset();
            if (modelAsset != null)
            {
                FieldInfo modelField = predictorType.GetField("modelAsset", 
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    
                if (modelField != null)
                {
                    modelField.SetValue(predictor, modelAsset);
                    Debug.Log($"Set model asset on DeepLabPredictor: {modelAsset.name}");
                }
            }
            
            Debug.Log("Successfully created DeepLabPredictor as fallback");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating DeepLabPredictor: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
    
    private void SetupARSystem()
    {
        // Check for existing ARSession and XROrigin
        ARSession arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        
        // Create AR System container if it doesn't exist
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            arSystem = new GameObject("AR System");
            Debug.Log("Created AR System container");
        }
        
        // Add ARSystemManager if needed
        Type arSystemManagerType = Type.GetType("ARSystemManager, Assembly-CSharp");
        if (arSystemManagerType != null && arSystem.GetComponent(arSystemManagerType) == null)
        {
            arSystem.AddComponent(arSystemManagerType);
            Debug.Log("Added ARSystemManager to AR System");
        }
        
        // Create AR Session if needed
        if (arSession == null)
        {
            GameObject arSessionObj = new GameObject("AR Session");
            arSessionObj.transform.SetParent(arSystem.transform, false);
            
            arSession = arSessionObj.AddComponent<ARSession>();
            arSessionObj.AddComponent<ARInputManager>();
            
            Debug.Log("Created AR Session");
        }
        else if (arSession.transform.parent != arSystem.transform)
        {
            // Move existing AR Session under AR System
            arSession.transform.SetParent(arSystem.transform, true);
            Debug.Log("Moved existing AR Session under AR System");
        }
        
        // Create XR Origin if needed
        GameObject xrOriginObj = null;
        if (xrOrigin == null)
        {
            xrOriginObj = new GameObject("XR Origin");
            xrOriginObj.transform.SetParent(arSystem.transform, false);
            
            xrOrigin = xrOriginObj.AddComponent<XROrigin>();
            
            // Add required AR components
            xrOriginObj.AddComponent<ARPlaneManager>();
            xrOriginObj.AddComponent<ARRaycastManager>();
            
            // Create Camera Offset
            GameObject cameraOffsetObj = new GameObject("Camera Offset");
            cameraOffsetObj.transform.SetParent(xrOriginObj.transform, false);
            
            // Create AR Camera
            GameObject arCameraObj = new GameObject("AR Camera");
            arCameraObj.transform.SetParent(cameraOffsetObj.transform, false);
            
            // Configure camera
            Camera arCamera = arCameraObj.AddComponent<Camera>();
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.1f;
            arCamera.farClipPlane = 20f;
            arCameraObj.tag = "MainCamera";
            
            // Add AR Camera components
            ARCameraManager cameraManager = arCameraObj.AddComponent<ARCameraManager>();
            arCameraObj.AddComponent<ARCameraBackground>();
            
            #if UNITY_IOS || UNITY_ANDROID
            // Use auto-focus on mobile
            cameraManager.autoFocusRequested = true;
            #endif
            
            // Configure XR Origin
            xrOrigin.Camera = arCamera;
            xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;
            
            Debug.Log("Created XR Origin with AR Camera");
        }
        else
        {
            xrOriginObj = xrOrigin.gameObject;
            
            // Move existing XR Origin under AR System if needed
            if (xrOrigin.transform.parent != arSystem.transform)
            {
                xrOrigin.transform.SetParent(arSystem.transform, true);
                Debug.Log("Moved existing XR Origin under AR System");
            }
        }
        
        // Ensure XR Origin has all required components
        if (xrOriginObj.GetComponent<ARPlaneManager>() == null)
            xrOriginObj.AddComponent<ARPlaneManager>();
            
        if (xrOriginObj.GetComponent<ARRaycastManager>() == null)
            xrOriginObj.AddComponent<ARRaycastManager>();
            
        // Create AR Mesh Manager as child of XR Origin if needed
        bool hasMeshManager = false;
        foreach (Transform child in xrOrigin.transform)
        {
            if (child.GetComponent<ARMeshManager>() != null)
            {
                hasMeshManager = true;
                break;
            }
        }
        
        if (!hasMeshManager)
        {
            GameObject meshManagerObj = new GameObject("AR Mesh Manager");
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            ARMeshManager meshManager = meshManagerObj.AddComponent<ARMeshManager>();
            meshManager.density = 0.5f;
            Debug.Log("Created AR Mesh Manager");
        }
        
        // Set up UI Canvas if using camera space
        if (useCameraSpaceCanvas)
        {
            GameObject uiCanvas = GameObject.Find("UI Canvas");
            
            if (uiCanvas == null)
            {
                uiCanvas = new GameObject("UI Canvas");
                Canvas canvas = uiCanvas.AddComponent<Canvas>();
                
                if (xrOrigin.Camera != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = xrOrigin.Camera;
                    canvas.planeDistance = 1.0f;
                }
                else
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }
                
                uiCanvas.AddComponent<CanvasScaler>();
                uiCanvas.AddComponent<GraphicRaycaster>();
                
                Debug.Log("Created UI Canvas");
            }
        }
    }
    
    private void FixARCamera()
    {
        // Find XR Origin
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogWarning("Cannot fix AR Camera - XR Origin or Camera not found");
            return;
        }
        
        // Get the camera object
        GameObject cameraObj = xrOrigin.Camera.gameObject;
        
        // Ensure it has ARCameraManager
        if (cameraObj.GetComponent<ARCameraManager>() == null)
        {
            ARCameraManager cameraManager = cameraObj.AddComponent<ARCameraManager>();
            #if UNITY_IOS || UNITY_ANDROID
            cameraManager.autoFocusRequested = true;
            #endif
            Debug.Log("Added ARCameraManager to AR Camera");
        }
        
        // Ensure it has ARCameraBackground
        if (cameraObj.GetComponent<ARCameraBackground>() == null)
        {
            cameraObj.AddComponent<ARCameraBackground>();
            Debug.Log("Added ARCameraBackground to AR Camera");
        }
        
        // Check for TrackedPoseDriver - needed to update camera position from AR device
        AddTrackedPoseDriverIfNeeded(cameraObj);
    }
    
    private void AddTrackedPoseDriverIfNeeded(GameObject cameraObj)
    {
        // First try with direct type check (Input System TrackedPoseDriver)
        Type tpdType = Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
        Component existingTPD = null;
        
        if (tpdType != null)
        {
            existingTPD = cameraObj.GetComponent(tpdType);
        }
        
        // If not found, try legacy version
        if (existingTPD == null)
        {
            Type legacyTPDType = Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, Unity.XR.CoreUtils");
            if (legacyTPDType != null)
            {
                existingTPD = cameraObj.GetComponent(legacyTPDType);
            }
        }
        
        // If no TrackedPoseDriver of any kind found, try to add one
        if (existingTPD == null)
        {
            try
            {
                // Try to add Input System version first (newer)
                if (tpdType != null)
                {
                    Component tpd = cameraObj.AddComponent(tpdType);
                    
                    // Try to configure the TPD using reflection since we don't have direct type access
                    tpdType.GetField("positionAction")?.SetValue(tpd, GetDefaultAction("devicePosition"));
                    tpdType.GetField("rotationAction")?.SetValue(tpd, GetDefaultAction("deviceRotation"));
                    
                    Debug.Log("Added Input System TrackedPoseDriver to AR Camera");
                    return;
                }
                
                // Fall back to legacy version
                Type legacyTPDType = Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, Unity.XR.CoreUtils");
                if (legacyTPDType != null)
                {
                    cameraObj.AddComponent(legacyTPDType);
                    Debug.Log("Added Legacy TrackedPoseDriver to AR Camera");
                    return;
                }
                
                Debug.LogWarning("Could not add any TrackedPoseDriver to AR Camera - AR tracking may not work correctly");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error adding TrackedPoseDriver: {ex.Message}");
            }
        }
    }
    
    private object GetDefaultAction(string actionName)
    {
        try
        {
            // Use reflection to create InputAction objects
            Type inputActionType = Type.GetType("UnityEngine.InputSystem.InputAction, Unity.InputSystem");
            if (inputActionType == null) return null;
            
            object action = Activator.CreateInstance(inputActionType);
            
            // Set the name
            inputActionType.GetProperty("name")?.SetValue(action, actionName);
            
            return action;
        }
        catch
        {
            return null;
        }
    }
    
    private void SetupMLSystem()
    {
        // Find AR System
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            Debug.LogError("AR System not found! Cannot set up ML System");
            return;
        }
        
        // Check if ML System already exists
        Transform existingMLSystem = arSystem.transform.Find("ML System");
        if (existingMLSystem != null)
        {
            Debug.Log("Using existing ML System");
            SetupEnhancedMLComponents(existingMLSystem.gameObject);
            return;
        }
        
        // Create ML System parent
        GameObject mlSystem = new GameObject("ML System");
        mlSystem.transform.SetParent(arSystem.transform, false);
        
        // Create SegmentationManager
        GameObject segmentationManagerObj = new GameObject("SegmentationManager");
        segmentationManagerObj.transform.SetParent(mlSystem.transform, false);
        segmentationManagerObj.AddComponent<SegmentationManager>();
        
        // Create MaskProcessor
        GameObject maskProcessorObj = new GameObject("MaskProcessor");
        maskProcessorObj.transform.SetParent(mlSystem.transform, false);
        maskProcessorObj.AddComponent<MaskProcessor>();
        
        // Create MLConnector
        GameObject mlConnectorObj = new GameObject("MLConnector");
        mlConnectorObj.transform.SetParent(mlSystem.transform, false);
        MLConnector mlConnector = mlConnectorObj.AddComponent<MLConnector>();
        
        // Создаем MLManagerAdapter для связи между ARMLController и SegmentationManager
        GameObject mlManagerAdapterObj = new GameObject("MLManagerAdapter");
        mlManagerAdapterObj.transform.SetParent(mlSystem.transform, false);
        MLManagerAdapter mlManagerAdapter = mlManagerAdapterObj.AddComponent<MLManagerAdapter>();
        
        // Находим необходимые компоненты для настройки MLManagerAdapter
        SegmentationManager segManager = segmentationManagerObj.GetComponent<SegmentationManager>();
        MaskProcessor maskProcessor = maskProcessorObj.GetComponent<MaskProcessor>();
        ARCameraManager cameraManager = null;
        
        // Найти ARCameraManager
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
        }
        
        // Настроить MLManagerAdapter через SerializedObject
        if (mlManagerAdapter != null)
        {
            SerializedObject serializedAdapter = new SerializedObject(mlManagerAdapter);
            
            SerializedProperty arCameraManagerProp = serializedAdapter.FindProperty("arCameraManager");
            if (arCameraManagerProp != null && cameraManager != null)
            {
                arCameraManagerProp.objectReferenceValue = cameraManager;
            }
            
            SerializedProperty segManagerProp = serializedAdapter.FindProperty("segmentationManager");
            if (segManagerProp != null && segManager != null)
            {
                segManagerProp.objectReferenceValue = segManager;
            }
            
            SerializedProperty maskProcessorProp = serializedAdapter.FindProperty("maskProcessor");
            if (maskProcessorProp != null && maskProcessor != null)
            {
                maskProcessorProp.objectReferenceValue = maskProcessor;
            }
            
            // Включаем режим отладки для отслеживания работы
            SerializedProperty debugModeProp = serializedAdapter.FindProperty("debugMode");
            if (debugModeProp != null)
            {
                debugModeProp.boolValue = true;
            }
            
            serializedAdapter.ApplyModifiedProperties();
            Debug.Log("Настроен MLManagerAdapter для связи компонентов AR и ML");
        }
        
        // Find and assign ARCameraManager reference
        if (cameraManager != null && mlConnector != null)
        {
            SerializedObject serializedConnector = new SerializedObject(mlConnector);
            SerializedProperty cameraProp = serializedConnector.FindProperty("arCameraManager");
            if (cameraProp != null)
            {
                cameraProp.objectReferenceValue = cameraManager;
                serializedConnector.ApplyModifiedProperties();
            }
        }
        
        // Setup enhanced ML components
        SetupEnhancedMLComponents(mlSystem);
        
        Debug.Log("Created ML System with components");
    }
    
    private void SetupEnhancedMLComponents(GameObject mlSystem)
    {
        // Check if we need to create additional ML components
        try 
        {
            // Try to create ARMLController if the type exists
            Type armlControllerType = Type.GetType("ARMLController, Assembly-CSharp");
            if (armlControllerType != null)
            {
                // Check if it already exists
                Component existingController = mlSystem.GetComponentInChildren(armlControllerType);
                if (existingController == null)
                {
                    GameObject controllerObj = new GameObject("ARMLController");
                    controllerObj.transform.SetParent(mlSystem.transform, false);
                    controllerObj.AddComponent(armlControllerType);
                    Debug.Log("Created ARMLController component");
                }
            }
            
            // Try to create EnhancedDeepLabPredictor if the type exists
            Type predictorType = Type.GetType("EnhancedDeepLabPredictor, Assembly-CSharp");
            if (predictorType != null)
            {
                // Check if it already exists
                Component existingPredictor = mlSystem.GetComponentInChildren(predictorType);
                if (existingPredictor == null)
                {
                    GameObject predictorObj = new GameObject("EnhancedDeepLabPredictor");
                    predictorObj.transform.SetParent(mlSystem.transform, false);
                    predictorObj.AddComponent(predictorType);
                    Debug.Log("Created EnhancedDeepLabPredictor component");
                }
            }
            
            // Connect MLConnector with these components
            MLConnector mlConnector = mlSystem.GetComponentInChildren<MLConnector>();
            if (mlConnector != null)
            {
                // Mark MLConnector to be updated
                EditorUtility.SetDirty(mlConnector);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error creating enhanced ML components: {ex.Message}");
        }
    }
    
    private void AddMLDebugViewer()
    {
        // Find UI Canvas
        GameObject uiCanvas = GameObject.Find("UI Canvas");
        if (uiCanvas == null)
        {
            Debug.LogWarning("UI Canvas not found! Cannot add ML Debug Viewer");
            return;
        }
        
        // Check if debug viewer already exists
        Transform existingViewer = uiCanvas.transform.Find("ML Debug Viewer");
        if (existingViewer != null)
        {
            Debug.Log("ML Debug Viewer already exists");
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
        
        // Find MLConnector and connect the debugger
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
        
        Debug.Log("Added ML Debug Viewer");
    }
    
    private void EnsureCorrectHierarchy()
    {
        Debug.Log("Проверяем правильность иерархии AR объектов...");
        
        // Находим основные AR объекты
        GameObject arSystem = GameObject.Find("AR System");
        GameObject xrOriginObj = GameObject.Find("XR Origin");
        GameObject arSessionObj = GameObject.Find("AR Session");
        
        if (arSystem == null)
        {
            Debug.LogWarning("Объект AR System не найден, но должен был быть создан раньше.");
            return;
        }
        
        if (xrOriginObj == null)
        {
            Debug.LogWarning("Объект XR Origin не найден, но должен был быть создан раньше.");
            return;
        }
        
        // Исправляем ссылку на камеру в XROrigin
        XROrigin xrOrigin = xrOriginObj.GetComponent<XROrigin>();
        if (xrOrigin != null)
        {
            FixXROriginCameraReference(xrOrigin);
        }
        
        // Проверяем ARMeshManager
        ARMeshManager[] meshManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsSortMode.None);
        bool hasMeshManager = false;
        
        foreach (ARMeshManager manager in meshManagers)
        {
            if (manager.gameObject == xrOriginObj)
            {
                hasMeshManager = true;
                break;
            }
            else
            {
                // Удаляем неправильно размещенные ARMeshManager
                Debug.LogWarning($"Удаляем ARMeshManager с объекта {manager.gameObject.name}, он должен быть на XR Origin");
                UnityEngine.Object.DestroyImmediate(manager);
            }
        }
        
        // Если нет ARMeshManager на XROrigin, добавляем
        if (!hasMeshManager && xrOriginObj != null)
        {
            xrOriginObj.AddComponent<ARMeshManager>();
            Debug.Log("Добавлен ARMeshManager на XR Origin");
        }
        
        // Проверяем, чтобы XROrigin и ARSession были дочерними объектами AR System
        if (xrOriginObj != null && xrOriginObj.transform.parent != arSystem.transform)
        {
            xrOriginObj.transform.SetParent(arSystem.transform, true);
            Debug.Log("XR Origin перемещен под AR System");
        }
        
        if (arSessionObj != null && arSessionObj.transform.parent != arSystem.transform)
        {
            arSessionObj.transform.SetParent(arSystem.transform, true);
            Debug.Log("AR Session перемещен под AR System");
        }
    }
    
    private void FixXROriginCameraReference(XROrigin xrOrigin)
    {
        if (xrOrigin == null)
        {
            Debug.LogError("XROrigin is null, cannot fix camera reference");
            return;
        }
        
        // Check if the camera reference is already set
        if (xrOrigin.Camera != null)
        {
            Debug.Log("XROrigin already has camera reference");
            return;
        }
        
        Debug.LogWarning("XROrigin has no camera reference, fixing...");
        
        // Use SerializedObject to directly access the private camera field
        SerializedObject serializedXROrigin = new SerializedObject(xrOrigin);
        
        // First, find or create the Camera Offset GameObject
        GameObject cameraOffset = null;
        SerializedProperty cameraOffsetProperty = serializedXROrigin.FindProperty("m_CameraFloorOffsetObject");
        if (cameraOffsetProperty != null && cameraOffsetProperty.objectReferenceValue != null)
        {
            cameraOffset = cameraOffsetProperty.objectReferenceValue as GameObject;
        }
        
        if (cameraOffset == null)
        {
            // Look for Camera Offset as child of XROrigin
            Transform cameraOffsetTransform = xrOrigin.transform.Find("Camera Offset");
            if (cameraOffsetTransform != null)
            {
                cameraOffset = cameraOffsetTransform.gameObject;
            }
            else
            {
                // Create Camera Offset if it doesn't exist
                cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOrigin.transform, false);
                cameraOffset.transform.localPosition = Vector3.zero;
                cameraOffset.transform.localRotation = Quaternion.identity;
                cameraOffset.transform.localScale = Vector3.one;
                
                Debug.Log("Created new Camera Offset GameObject");
            }
            
            // Set the property value
            cameraOffsetProperty.objectReferenceValue = cameraOffset;
        }
        
        // Now find or create AR Camera
        Camera arCamera = null;
        
        // First check if there's an AR Camera under Camera Offset
        if (cameraOffset != null)
        {
            Transform arCameraTransform = cameraOffset.transform.Find("AR Camera");
            if (arCameraTransform != null)
            {
                arCamera = arCameraTransform.GetComponent<Camera>();
                
                if (arCamera == null)
                {
                    // Found the transform but no camera component, add it
                    arCamera = arCameraTransform.gameObject.AddComponent<Camera>();
                    Debug.Log("Added Camera component to existing AR Camera GameObject");
                }
            }
            else
            {
                // No AR Camera under Camera Offset, create one
                GameObject arCameraObj = new GameObject("AR Camera");
                arCameraObj.transform.SetParent(cameraOffset.transform, false);
                arCameraObj.transform.localPosition = Vector3.zero;
                arCameraObj.transform.localRotation = Quaternion.identity;
                arCameraObj.transform.localScale = Vector3.one;
                
                arCamera = arCameraObj.AddComponent<Camera>();
                arCamera.clearFlags = CameraClearFlags.SolidColor;
                arCamera.backgroundColor = Color.black;
                arCamera.nearClipPlane = 0.1f;
                arCamera.farClipPlane = 20f;
                arCameraObj.tag = "MainCamera";
                
                // Add AR Camera components
                ARCameraManager cameraManager = arCameraObj.AddComponent<ARCameraManager>();
                arCameraObj.AddComponent<ARCameraBackground>();
                AddTrackedPoseDriverIfNeeded(arCameraObj);
                
                #if UNITY_IOS || UNITY_ANDROID
                // Use auto-focus on mobile
                cameraManager.autoFocusRequested = true;
                #endif
                
                Debug.Log("Created new AR Camera GameObject");
            }
            
            // Set the camera reference in XROrigin
            SerializedProperty cameraProperty = serializedXROrigin.FindProperty("m_Camera");
            if (cameraProperty != null)
            {
                cameraProperty.objectReferenceValue = arCamera;
                Debug.Log("Set camera reference in XROrigin");
            }
            
            // Apply the changes
            serializedXROrigin.ApplyModifiedProperties();
            
            // Verify the changes
            if (xrOrigin.Camera == null)
            {
                Debug.LogError("Failed to set camera reference in XROrigin through SerializedObject");
                
                // As a fallback, try to modify the field directly using reflection
                var cameraField = xrOrigin.GetType().GetField("m_Camera", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (cameraField != null)
                {
                    cameraField.SetValue(xrOrigin, arCamera);
                    Debug.Log("Set camera reference in XROrigin using reflection");
                }
            }
            else
            {
                Debug.Log("Successfully set camera reference in XROrigin");
            }
        }
        else
        {
            Debug.LogError("Failed to find or create Camera Offset GameObject");
        }
    }

    /// <summary>
    /// Добавляет объект с компонентом ARRuntimeFixer для исправления проблем во время выполнения
    /// </summary>
    private void AddARRuntimeFixer()
    {
        // Проверяем, есть ли уже ARRuntimeFixer в сцене
        ARRuntimeFixer existingFixer = UnityEngine.Object.FindFirstObjectByType<ARRuntimeFixer>();
        if (existingFixer != null)
        {
            Debug.Log("ARRuntimeFixer already exists in scene");
            return;
        }
        
        // Создаем GameObject с компонентом ARRuntimeFixer
        GameObject fixerObj = new GameObject("AR Runtime Fixer");
        ARRuntimeFixer fixer = fixerObj.AddComponent<ARRuntimeFixer>();
        
        // Добавляем DontDestroyOnLoad, чтобы объект не уничтожался при переходе между сценами
        fixerObj.AddComponent<DontDestroyOnLoad>();
        
        Debug.Log("Added ARRuntimeFixer to scene");
        configStatus = configStatus + "\nAdded AR Runtime Fixer for automatic problem fixing at runtime";
    }

    /// <summary>
    /// Создает визуализатор AR-плоскостей
    /// </summary>
    private void CreateARPlaneVisualizer()
    {
        try
        {
            // Проверяем, есть ли в сцене AR Plane Manager
            if (!EnsureARPlaneManagerExists())
            {
                configStatus = "Не удалось найти или создать ARPlaneManager";
                statusType = MessageType.Error;
                Debug.LogError("Невозможно добавить визуализатор плоскостей без ARPlaneManager");
                return;
            }
            
            // Проверка, существует ли уже компонент ARPlaneVisualizer
            Component[] planeVisualizers = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None);
            foreach (Component comp in planeVisualizers)
            {
                if (comp.GetType().Name.Contains("ARPlaneVisualizer") || 
                    comp.GetType().Name.Contains("PlaneVisualizer"))
                {
                    // Выводим сообщение в лог вместо диалога
                    Debug.Log("Визуализатор AR-плоскостей уже существует в сцене");
                    configStatus = "Визуализатор AR-плоскостей уже существует";
                    statusType = MessageType.Info;
                    return;
                }
            }
            
            // Ищем ARPlaneManager
            ARPlaneManager arPlaneManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
            if (arPlaneManager == null)
            {
                configStatus = "ARPlaneManager не найден в сцене";
                statusType = MessageType.Error;
                Debug.LogError("ARPlaneManager не найден. Невозможно добавить визуализатор плоскостей.");
                return;
            }
            
            // Создаем объект для визуализатора
            GameObject visualizerObj = new GameObject("AR Plane Visualizer");
            
            // Можно использовать разные типы визуализаторов, проверяем по наличию типов
            Type visualizerType = 
                Type.GetType("ARPlaneVisualizer, Assembly-CSharp") ?? 
                Type.GetType("PlaneVisualizer, Assembly-CSharp") ?? 
                Type.GetType("SurfaceVisualizer, Assembly-CSharp");
            
            if (visualizerType != null)
            {
                // Добавляем компонент визуализатора
                Component visualizer = visualizerObj.AddComponent(visualizerType);
                
                // Пытаемся установить ссылку на ARPlaneManager
                FieldInfo planeManagerField = visualizerType.GetField("planeManager", 
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (planeManagerField != null)
                {
                    planeManagerField.SetValue(visualizer, arPlaneManager);
                }
                
                Debug.Log("Визуализатор AR-плоскостей успешно создан");
                configStatus = "Визуализатор AR-плоскостей добавлен";
                statusType = MessageType.Info;
            }
            else
            {
                // Создаем простой визуализатор, который добавляет MeshRenderer к AR плоскостям
                visualizerObj.AddComponent<ARPlaneMeshVisualizer>();
                
                Debug.Log("Создан базовый визуализатор AR-плоскостей");
                
                // Предупреждаем в лог вместо диалога
                Debug.LogWarning("Не удалось найти полнофункциональный ARPlaneVisualizer. " +
                    "Создан базовый визуализатор, который отображает только меши плоскостей.");
                
                configStatus = "Создан базовый визуализатор AR-плоскостей";
                statusType = MessageType.Warning;
            }
            
            // Включаем ARPlaneManager, если он выключен
            if (!arPlaneManager.enabled)
            {
                arPlaneManager.enabled = true;
                Debug.Log("ARPlaneManager был включен");
            }
            
            // Добавляем исправитель проблем AR-плоскостей
            AddARPlaneVisualizerFixer();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Ошибка при создании визуализатора AR-плоскостей: {e.Message}\n{e.StackTrace}");
            configStatus = "Ошибка создания визуализатора AR-плоскостей";
            statusType = MessageType.Error;
        }
    }

    /// <summary>
    /// Простой визуализатор AR-плоскостей использующий меши
    /// </summary>
    public class ARPlaneMeshVisualizer : MonoBehaviour
    {
        private ARPlaneManager planeManager;
        private Material planeMaterial;
        private Color planeColor = new Color(0.3f, 0.4f, 0.6f, 0.5f);
        
        private void Start()
        {
            // Находим ARPlaneManager
            planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager != null)
            {
                // Подписываемся на событие обновления плоскостей
                planeManager.planesChanged += OnPlanesChanged;
                
                // Создаем материал для плоскостей
                planeMaterial = new Material(Shader.Find("Unlit/Transparent"));
                planeMaterial.color = planeColor;
            }
            else
            {
                Debug.LogError("ARPlaneManager не найден");
                enabled = false;
            }
        }
        
        private void OnDestroy()
        {
            if (planeManager != null)
            {
                planeManager.planesChanged -= OnPlanesChanged;
            }
        }
        
        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            // Добавляем материалы к новым плоскостям
            foreach (ARPlane plane in args.added)
            {
                SetPlaneMaterial(plane);
            }
            
            // Обновляем материалы у измененных плоскостей
            foreach (ARPlane plane in args.updated)
            {
                SetPlaneMaterial(plane);
            }
        }
        
        private void SetPlaneMaterial(ARPlane plane)
        {
            // Проверяем наличие MeshRenderer и устанавливаем материал
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = planeMaterial;
            }
            else
            {
                // Если рендерера нет, добавляем его
                renderer = plane.gameObject.AddComponent<MeshRenderer>();
                renderer.material = planeMaterial;
            }
        }
    }

    /// <summary>
    /// Добавляет компонент ARPlaneVisualizerFixer для исправления визуализации плоскостей
    /// </summary>
    private void AddARPlaneVisualizerFixer()
    {
        // Проверяем, существует ли уже объект с компонентом ARPlaneVisualizerFixer
        ARPlaneVisualizerFixer existingFixer = UnityEngine.Object.FindFirstObjectByType<ARPlaneVisualizerFixer>();
        
        if (existingFixer != null)
        {
            Debug.Log("ARPlaneVisualizerFixer уже присутствует в сцене");
            return;
        }
        
        // Ищем AR System, чтобы добавить к нему фиксер
        GameObject arSystem = GameObject.Find("AR System");
        
        if (arSystem == null)
        {
            Debug.LogWarning("Не найден AR System для добавления компонента ARPlaneVisualizerFixer");
            return;
        }
        
        // Создаем объект для фиксера и добавляем его в AR System
        GameObject fixerObject = new GameObject("AR Plane Visualizer Fixer");
        fixerObject.transform.SetParent(arSystem.transform, false);
        
        // Добавляем компонент фиксера
        fixerObject.AddComponent<ARPlaneVisualizerFixer>();
        
        Debug.Log("Добавлен компонент ARPlaneVisualizerFixer для улучшения визуализации плоскостей");
    }

    private bool EnsureARPlaneManagerExists()
    {
        // Проверяем существует ли ARPlaneManager
        ARPlaneManager planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
        
        if (planeManager != null)
        {
            // Уже существует
            return true;
        }
        
        // Ищем XROrigin
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("Не найден XROrigin. Пожалуйста, сначала настройте AR сцену.");
            configStatus = "Не найден XROrigin для ARPlaneManager";
            statusType = MessageType.Error;
            return false;
        }
        
        // Добавляем ARPlaneManager к XROrigin
        planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
        Debug.Log("ARPlaneManager добавлен к " + xrOrigin.gameObject.name);
        
        // Включаем его
        planeManager.enabled = true;
        
        // Добавляем ARPointCloudManager для улучшения распознавания плоскостей
        if (xrOrigin.gameObject.GetComponent<ARPointCloudManager>() == null)
        {
            var pointCloudManager = xrOrigin.gameObject.AddComponent<ARPointCloudManager>();
            pointCloudManager.enabled = true;
            Debug.Log("ARPointCloudManager добавлен для улучшения распознавания плоскостей");
        }
        
        // Отмечаем объект как "грязный" для сохранения изменений
        EditorUtility.SetDirty(xrOrigin.gameObject);
        
        configStatus = "Создан ARPlaneManager";
        statusType = MessageType.Info;
        return true;
    }

    /// <summary>
    /// Настраивает ARSystemManager, если он нужен в проекте
    /// </summary>
    private void SetupARSystemManager()
    {
        if (GUILayout.Button("Добавить AR System Manager", GUILayout.Height(30)))
        {
            // Проверяем, существует ли ARSystemManager
            GameObject existingManager = GameObject.Find("AR System Manager");
            if (existingManager != null)
            {
                Debug.Log("AR System Manager уже существует в сцене.");
                configStatus = "AR System Manager уже существует";
                statusType = MessageType.Info;
                return;
            }
            
            // Создаем новый объект для ARSystemManager
            GameObject managerObj = new GameObject("AR System Manager");
            
            // Пытаемся найти тип ARSystemManager через рефлексию
            Type managerType = Type.GetType("ARSystemManager, Assembly-CSharp");
            
            if (managerType != null)
            {
                managerObj.AddComponent(managerType);
                Debug.Log("Добавлен AR System Manager");
                
                // Находим и связываем с AR сущностями, если нужно
                XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null)
                {
                    // Если в компоненте есть поле для XROrigin, устанавливаем его
                    Component manager = managerObj.GetComponent(managerType);
                    FieldInfo xrOriginField = managerType.GetField("xrOrigin");
                    if (xrOriginField != null)
                    {
                        xrOriginField.SetValue(manager, xrOrigin);
                        Debug.Log("AR System Manager связан с XR Origin");
                    }
                }
                
                configStatus = "AR System Manager успешно добавлен";
                statusType = MessageType.Info;
            }
            else
            {
                Debug.LogWarning("Тип ARSystemManager не найден. Возможно, он был удален или переименован.");
                configStatus = "Не удалось найти тип ARSystemManager";
                statusType = MessageType.Warning;
                GameObject.DestroyImmediate(managerObj);
            }
        }
    }
    
    /// <summary>
    /// Создает основные AR компоненты в сцене
    /// </summary>
    private GameObject CreateARComponents()
    {
        try
        {
            // Создаем родительский объект для AR системы
            GameObject arSystem = GameObject.Find("AR System");
            if (arSystem == null)
            {
                arSystem = new GameObject("AR System");
                Debug.Log("Создан контейнер AR System");
            }
            
            // Настраиваем AR систему и получаем XROrigin
            XROrigin xrOrigin = SetupARSystemCore();
            
            // Если XROrigin создан, делаем его дочерним для arSystem
            if (xrOrigin != null && xrOrigin.transform.parent != arSystem.transform)
            {
                xrOrigin.transform.SetParent(arSystem.transform, true);
                Debug.Log("XR Origin добавлен в контейнер AR System");
            }
            
            // Находим ARSession и тоже прикрепляем к arSystem
            ARSession arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (arSession != null && arSession.transform.parent != arSystem.transform)
            {
                arSession.transform.SetParent(arSystem.transform, true);
                Debug.Log("AR Session добавлен в контейнер AR System");
            }
            
            return arSystem;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Ошибка при создании AR компонентов: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Внутренний метод для настройки основных AR компонентов
    /// </summary>
    private XROrigin SetupARSystemCore()
    {
        // Настройка основной AR системы - можно использовать существующий код из SetupARSystem
        // или создать новую реализацию, в зависимости от требований
        return UnityEngine.Object.FindFirstObjectByType<XROrigin>();
    }

    public void SetupARSceneAllInOne()
    {
        Debug.Log("Начинаем полную настройку AR сцены...");

        // 1. Удаление дубликатов ARSession
        CleanupDuplicateARSessions();

        // 2. Создание основных AR компонентов
        GameObject arSystem = CreateARComponents();

        // 3. Находим XROrigin
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("XROrigin не найден после создания AR компонентов. Проверьте настройки проекта.");
            configStatus = "Ошибка: XROrigin не найден";
            statusType = MessageType.Error;
            return;
        }

        // 4. Проверка и настройка XROrigin и его камеры
        FixXROriginCameraReference(xrOrigin);

        // 5. Проверка иерархии объектов
        EnsureCorrectHierarchy();

        // 6. Настройка ARMLController и ML компонентов
        FixARMLControllerReferences();

        // 7. Настройка визуализатора плоскостей
        CreateARPlaneVisualizer();

        // 8. Добавление фиксера визуализации плоскостей
        AddARPlaneVisualizerFixer();

        // 9. Добавление компонента ARRuntimeFixer
        AddARRuntimeFixer();

        // 10. Добавление TrackedPoseDriver к камере, если его нет
        if (xrOrigin.Camera != null)
        {
            AddTrackedPoseDriverIfNeeded(xrOrigin.Camera.gameObject);
        }
        
        // 11. Повторная проверка дубликатов ARSession для гарантии отсутствия конфликтов
        CleanupDuplicateARSessions();

        Debug.Log("Полная настройка AR сцены завершена успешно!");
        
        // Отмечаем сцену как измененную, чтобы пользователь мог сохранить изменения
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        
        configStatus = "Настройка AR сцены завершена успешно!";
        statusType = MessageType.Info;
    }
} 