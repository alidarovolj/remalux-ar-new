using UnityEngine;
using UnityEditor;
using ML.DeepLab;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Редактор-скрипт для добавления и настройки оптимизаций обнаружения стен через меню
/// </summary>
public class WallOptimizerMenu : MonoBehaviour
{
    private const string MENU_BASE = "AR Tools/Wall Optimizer/";
    
    /// <summary>
    /// Добавить и настроить оптимизатор стен
    /// </summary>
    [MenuItem(MENU_BASE + "Setup Wall Optimizer")]
    public static void SetupWallOptimizer()
    {
        // Проверяем наличие необходимых компонентов в сцене
        WallOptimizer existingOptimizer = Object.FindObjectOfType<WallOptimizer>();
        EnhancedDeepLabPredictor predictor = Object.FindObjectOfType<EnhancedDeepLabPredictor>();
        WallOptimizationManager existingManager = Object.FindObjectOfType<WallOptimizationManager>();
        ARRaycastManager raycastManager = Object.FindObjectOfType<ARRaycastManager>();
        ARAnchorManager anchorManager = Object.FindObjectOfType<ARAnchorManager>();
        ARPlaneManager planeManager = Object.FindObjectOfType<ARPlaneManager>();
        
        // Проверяем необходимость создания объекта оптимизации
        GameObject optimizationManager = null;
        WallOptimizationManager manager = null;
        
        if (existingManager != null)
        {
            // Используем существующий менеджер
            manager = existingManager;
            optimizationManager = manager.gameObject;
            Debug.Log("Найден существующий WallOptimizationManager, используем его");
        }
        else
        {
            // Создаем новый менеджер
            optimizationManager = new GameObject("Wall Optimization Manager");
            manager = optimizationManager.AddComponent<WallOptimizationManager>();
            Debug.Log("Создан новый WallOptimizationManager");
            
            // Размещаем в корне сцены
            optimizationManager.transform.SetAsLastSibling();
        }
        
        // Настраиваем ссылки на компоненты
        manager.wallOptimizer = existingOptimizer;
        manager.enhancedPredictor = predictor;
        manager.raycastManager = raycastManager;
        manager.anchorManager = anchorManager;
        manager.planeManager = planeManager;
        
        // Проверяем наличие критически важных компонентов
        List<string> missingComponents = new List<string>();
        
        if (existingOptimizer == null)
            missingComponents.Add("WallOptimizer");
        
        if (predictor == null)
            missingComponents.Add("EnhancedDeepLabPredictor");
        
        if (raycastManager == null)
            missingComponents.Add("ARRaycastManager");
            
        if (planeManager == null)
            missingComponents.Add("ARPlaneManager");
        
        // Выводим предупреждение о недостающих компонентах
        if (missingComponents.Count > 0)
        {
            string message = "Следующие компоненты не были найдены в сцене и требуют настройки:\n";
            foreach (string component in missingComponents)
            {
                message += $"- {component}\n";
            }
            
            EditorUtility.DisplayDialog("Внимание", message, "OK");
        }
        
        // Выбираем созданный объект в иерархии
        Selection.activeGameObject = optimizationManager;
        
        Debug.Log("Настройка оптимизатора стен завершена");
    }
    
    /// <summary>
    /// Включить/выключить безопасный режим
    /// </summary>
    [MenuItem(MENU_BASE + "Toggle Safe Mode")]
    public static void ToggleSafeMode()
    {
        WallOptimizationManager manager = Object.FindObjectOfType<WallOptimizationManager>();
        
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Ошибка", "WallOptimizationManager не найден в сцене. Сначала добавьте его через 'Setup Wall Optimizer'.", "OK");
            return;
        }
        
        // Включаем/выключаем безопасный режим
        bool currentMode = manager.enableARAttachment;
        manager.enableARAttachment = !currentMode;
        
        // Применяем изменения сразу
        manager.ApplyOptimizations();
        
        EditorUtility.SetDirty(manager);
        
        Debug.Log($"Безопасный режим {(manager.enableARAttachment ? "включен" : "выключен")}");
        EditorUtility.DisplayDialog("Wall Optimizer", $"Безопасный режим {(manager.enableARAttachment ? "включен" : "выключен")}", "OK");
    }
    
    /// <summary>
    /// Настроить оптимальную производительность
    /// </summary>
    [MenuItem(MENU_BASE + "Optimize Performance")]
    public static void OptimizePerformance()
    {
        WallOptimizationManager manager = Object.FindObjectOfType<WallOptimizationManager>();
        
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Ошибка", "WallOptimizationManager не найден в сцене. Сначала добавьте его через 'Setup Wall Optimizer'.", "OK");
            return;
        }
        
        // Настраиваем оптимальные параметры для производительности
        manager.downsamplingFactor = 2;
        manager.segmentationInterval = 0.3f; // Увеличиваем интервал для экономии ресурсов
        
        // Применяем изменения
        manager.ApplyOptimizations();
        
        EditorUtility.SetDirty(manager);
        
        Debug.Log("Применены оптимальные настройки производительности");
        EditorUtility.DisplayDialog("Wall Optimizer", "Применены оптимальные настройки производительности", "OK");
    }
} 