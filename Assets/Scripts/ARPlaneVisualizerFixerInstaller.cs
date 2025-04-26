using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

/// <summary>
/// Класс для автоматического добавления ARPlaneVisualizerFixer в активную сцену
/// Этот скрипт предназначен только для редактора Unity
/// </summary>
[InitializeOnLoad]
public class ARPlaneVisualizerFixerInstaller
{
    static ARPlaneVisualizerFixerInstaller()
    {
        // Подписываемся на событие загрузки сцены
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }
    
    private static void OnHierarchyChanged()
    {
        // Проверяем, существует ли объект с компонентом ARPlaneVisualizerFixer
        ARPlaneVisualizerFixer existingFixer = GameObject.FindObjectOfType<ARPlaneVisualizerFixer>();
        
        if (existingFixer == null)
        {
            // Если в сцене нет нашего фиксера, ищем AR System
            GameObject arSystem = GameObject.Find("AR System");
            
            if (arSystem != null)
            {
                // Проверяем, что в AR System есть ARPlaneManager
                bool hasPlaneManager = false;
                foreach (Transform child in arSystem.transform)
                {
                    if (child.GetComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>() != null)
                    {
                        hasPlaneManager = true;
                        break;
                    }
                }
                
                if (hasPlaneManager)
                {
                    // Создаем объект для фиксера
                    GameObject fixerObject = new GameObject("AR Plane Visualizer Fixer");
                    fixerObject.AddComponent<ARPlaneVisualizerFixer>();
                    
                    // Сделаем его дочерним для AR System
                    fixerObject.transform.SetParent(arSystem.transform, false);
                    
                    Debug.Log("ARPlaneVisualizerFixerInstaller: Автоматически добавлен ARPlaneVisualizerFixer в сцену");
                    
                    // Отмечаем сцену как измененную
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }
            }
        }
    }
}
#endif

/// <summary>
/// Компонент для автоматического добавления ARPlaneVisualizerFixer при запуске игры
/// </summary>
public class ARPlaneVisualizerFixerRuntimeInstaller : MonoBehaviour
{
    void Awake()
    {
        // Проверяем, существует ли объект с компонентом ARPlaneVisualizerFixer
        ARPlaneVisualizerFixer existingFixer = FindObjectOfType<ARPlaneVisualizerFixer>();
        
        if (existingFixer == null)
        {
            // Если в сцене нет нашего фиксера, ищем AR System
            GameObject arSystem = GameObject.Find("AR System");
            
            if (arSystem != null)
            {
                // Создаем объект для фиксера
                GameObject fixerObject = new GameObject("AR Plane Visualizer Fixer");
                fixerObject.AddComponent<ARPlaneVisualizerFixer>();
                
                // Сделаем его дочерним для AR System
                fixerObject.transform.SetParent(arSystem.transform, false);
                
                Debug.Log("ARPlaneVisualizerFixerInstaller: Автоматически добавлен ARPlaneVisualizerFixer в сцену при запуске");
            }
        }
    }
} 