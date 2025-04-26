using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Класс для добавления пунктов меню, связанных с ARPlaneVisualizerFixer
/// </summary>
public static class ARPlaneVisualizerFixerMenu
{
    [MenuItem("AR/Fix AR Plane Visualizer", priority = 100)]
    public static void FixARPlaneVisualizer()
    {
        // Проверяем, существует ли объект с компонентом ARPlaneVisualizerFixer
        ARPlaneVisualizerFixer existingFixer = UnityEngine.Object.FindFirstObjectByType<ARPlaneVisualizerFixer>();
        
        if (existingFixer != null)
        {
            // Если фиксер уже есть, используем его
            existingFixer.FixPlaneVisualizers();
            Debug.Log("Использован существующий ARPlaneVisualizerFixer");
            EditorUtility.DisplayDialog("AR Plane Visualizer", "Настройки визуализатора AR-плоскостей обновлены.", "OK");
            return;
        }
        
        // Ищем AR System
        GameObject arSystem = GameObject.Find("AR System");
        if (arSystem == null)
        {
            // Если нет AR System, пробуем найти XROrigin
            XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                // Если есть XROrigin, но нет AR System, создаем AR System
                arSystem = new GameObject("AR System");
                // Делаем XROrigin дочерним к AR System
                xrOrigin.transform.SetParent(arSystem.transform, true);
                Debug.Log("Создан новый AR System и XROrigin перемещен под него");
            }
            else
            {
                // Если ничего нет, сообщаем пользователю
                EditorUtility.DisplayDialog("AR Plane Visualizer", "AR System или XROrigin не найдены. Пожалуйста, сначала настройте AR сцену.", "OK");
                return;
            }
        }
        
        // Проверяем, есть ли ARPlaneManager
        ARPlaneManager planeManager = UnityEngine.Object.FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null)
        {
            // Если нет ARPlaneManager, предлагаем создать
            if (EditorUtility.DisplayDialog("AR Plane Visualizer", "ARPlaneManager не найден. Хотите создать его?", "Да", "Нет"))
            {
                // Ищем XROrigin для добавления ARPlaneManager
                XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
                if (xrOrigin == null)
                {
                    EditorUtility.DisplayDialog("AR Plane Visualizer", "XROrigin не найден. Необходимо сначала настроить AR сцену.", "OK");
                    return;
                }
                
                // Добавляем ARPlaneManager к XROrigin
                planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                Debug.Log("Добавлен ARPlaneManager к XROrigin");
            }
            else
            {
                return;
            }
        }
        
        // Создаем объект для фиксера
        GameObject fixerObject = new GameObject("AR Plane Visualizer Fixer");
        ARPlaneVisualizerFixer fixer = fixerObject.AddComponent<ARPlaneVisualizerFixer>();
        
        // Делаем фиксер дочерним к AR System
        fixerObject.transform.SetParent(arSystem.transform, false);
        
        // Запускаем исправление визуализаторов
        fixer.FixPlaneVisualizers();
        
        Debug.Log("Создан и запущен ARPlaneVisualizerFixer");
        EditorUtility.DisplayDialog("AR Plane Visualizer", "Визуализатор AR-плоскостей успешно настроен.", "OK");
        
        // Выделяем созданный объект
        Selection.activeGameObject = fixerObject;
    }
} 