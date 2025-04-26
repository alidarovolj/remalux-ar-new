using UnityEngine;

/// <summary>
/// Компонент для автоматического добавления ARPlaneVisualizerFixer при запуске игры
/// Используется в качестве самостоятельного объекта, создаваемого автоматически
/// </summary>
public class ARPlaneVisualizerFixerBoostrapper : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitAfterSceneLoad()
    {
        // Проверяем, существует ли объект с компонентом ARPlaneVisualizerFixer
        ARPlaneVisualizerFixer existingFixer = GameObject.FindObjectOfType<ARPlaneVisualizerFixer>();
        
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
                
                // Добавляем компонент, чтобы он не уничтожался при переходе между сценами
                fixerObject.AddComponent<DontDestroyOnLoad>();
                
                Debug.Log("ARPlaneVisualizerFixerBoostrapper: Автоматически добавлен ARPlaneVisualizerFixer в сцену при запуске");
            }
            else
            {
                // Если AR System не найден, создаем отдельный объект-фиксер
                GameObject fixerObject = new GameObject("AR Plane Visualizer Fixer (Standalone)");
                fixerObject.AddComponent<ARPlaneVisualizerFixer>();
                
                // Добавляем компонент, чтобы он не уничтожался при переходе между сценами
                fixerObject.AddComponent<DontDestroyOnLoad>();
                
                Debug.Log("ARPlaneVisualizerFixerBoostrapper: Автоматически добавлен автономный ARPlaneVisualizerFixer в сцену при запуске");
            }
        }
    }
} 