using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Автоматически добавляет ARMLInitializer в сцену, если его нет
/// </summary>
[InitializeOnLoad]
public class ARMLAutoSetup
{
    static ARMLAutoSetup()
    {
        // Подписываемся на событие открытия сцены
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    
    private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        // При открытии сцены проверяем наличие инициализатора
        CheckAndAddInitializer();
    }
    
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Проверяем перед запуском игры
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            CheckAndAddInitializer();
        }
    }
    
    private static void CheckAndAddInitializer()
    {
        // Проверяем, есть ли ARMLInitializer в сцене
        var initializer = Object.FindObjectOfType<ARMLInitializer>();
        if (initializer == null)
        {
            // Проверяем, есть ли в сцене объекты AR
            var xrOrigin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            var arSession = Object.FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
            
            if (xrOrigin != null || arSession != null)
            {
                // Спрашиваем пользователя, хочет ли он добавить инициализатор
                bool shouldAdd = EditorUtility.DisplayDialog(
                    "AR ML Инициализатор",
                    "В сцене обнаружены AR компоненты, но нет ARMLInitializer. " +
                    "Этот компонент исправляет проблемы с инициализацией модели ML и компонентов AR.\n\n" +
                    "Добавить ARMLInitializer в сцену?",
                    "Да, добавить", "Нет, спасибо");
                    
                if (shouldAdd)
                {
                    AddInitializer();
                }
            }
        }
    }
    
    [MenuItem("AR/Добавить ARMLInitializer")]
    private static void AddInitializerMenuItem()
    {
        AddInitializer();
    }
    
    private static void AddInitializer()
    {
        // Создаем объект с инициализатором
        GameObject initObj = new GameObject("AR ML Initializer");
        initObj.AddComponent<ARMLInitializer>();
        
        // Устанавливаем его как DontDestroyOnLoad
        var serializedObject = new SerializedObject(initObj);
        serializedObject.FindProperty("m_StaticEditorFlags").intValue |= 1; // Set DontSave flag
        serializedObject.ApplyModifiedProperties();
        
        // Добавляем его в текущую сцену
        Undo.RegisterCreatedObjectUndo(initObj, "Add AR ML Initializer");
        
        // Выбираем объект в иерархии
        Selection.activeGameObject = initObj;
        
        Debug.Log("ARMLAutoSetup: Добавлен ARMLInitializer в сцену");
        
        // Помечаем сцену как измененную
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
} 