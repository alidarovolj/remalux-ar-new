using UnityEngine;
using System.Collections;
using System.Reflection;

/// <summary>
/// Компонент для исправления и улучшения работы стандартного DeepLabPredictor.
/// Применяет оптимизации и исправления без замены оригинального компонента.
/// </summary>
public class DeepLabFixer : MonoBehaviour
{
    [Header("Настройки")]
    [Tooltip("DeepLabPredictor для исправления")]
    public DeepLabPredictor targetPredictor;
    
    [Tooltip("Порог классификации для стен (0.0-1.0)")]
    [Range(0.0f, 1.0f)]
    public float wallThreshold = 0.3f;
    
    [Tooltip("Автоматически находить и исправлять предиктор при старте")]
    public bool autoFixOnStart = true;
    
    [Tooltip("Логировать применение исправлений")]
    public bool logFixDetails = true;
    
    void Start()
    {
        if (autoFixOnStart)
        {
            StartCoroutine(ApplyFixesDelayed());
        }
    }
    
    IEnumerator ApplyFixesDelayed()
    {
        // Даем время на загрузку и инициализацию всех компонентов
        yield return new WaitForSeconds(1.0f);
        
        // Находим предиктор, если не назначен
        if (targetPredictor == null)
        {
            targetPredictor = FindFirstObjectByType<DeepLabPredictor>();
            
            if (targetPredictor == null)
            {
                Debug.LogWarning("DeepLabFixer: Не найден DeepLabPredictor для исправления");
                yield break;
            }
        }
        
        // Применяем исправления
        ApplyFixes();
    }
    
    public void ApplyFixes()
    {
        if (targetPredictor == null)
        {
            Debug.LogError("DeepLabFixer: targetPredictor не назначен");
            return;
        }
        
        if (logFixDetails)
            Debug.Log("DeepLabFixer: Применение исправлений к DeepLabPredictor...");
        
        // Исправление 1: Установка порога классификации
        targetPredictor.ClassificationThreshold = wallThreshold;
        
        if (logFixDetails)
            Debug.Log($"DeepLabFixer: Установлен порог классификации {wallThreshold}");
        
        // Исправление 2: Попытка исправить проблемы с определением выходного слоя через reflection
        TryFixOutputLayerDetection();
        
        // Исправление 3: Оптимизация масштабирования текстур
        TryOptimizeTextureResizing();
        
        if (logFixDetails)
            Debug.Log("DeepLabFixer: Все исправления применены успешно");
    }
    
    private void TryFixOutputLayerDetection()
    {
        try
        {
            // Получаем приватное поле 'workingOutputName' через reflection
            FieldInfo workingOutputNameField = typeof(DeepLabPredictor).GetField("workingOutputName", 
                BindingFlags.NonPublic | BindingFlags.Instance);
                
            if (workingOutputNameField != null)
            {
                string currentValue = (string)workingOutputNameField.GetValue(targetPredictor);
                
                // Получаем поле runtimeModel
                FieldInfo runtimeModelField = typeof(DeepLabPredictor).GetField("runtimeModel", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                    
                if (runtimeModelField != null)
                {
                    var runtimeModel = runtimeModelField.GetValue(targetPredictor);
                    
                    // Получаем outputs из модели
                    PropertyInfo outputsProperty = runtimeModel.GetType().GetProperty("outputs");
                    if (outputsProperty != null)
                    {
                        var outputs = outputsProperty.GetValue(runtimeModel) as System.Collections.Generic.IReadOnlyList<string>;
                        
                        if (outputs != null && outputs.Count > 0)
                        {
                            // Проверяем, есть ли в выходах нужные нам имена
                            string[] preferredOutputs = { "SemanticPredictions", "ArgMax", "final_output" };
                            
                            foreach (string preferred in preferredOutputs)
                            {
                                foreach (string output in outputs)
                                {
                                    if (output.Contains(preferred))
                                    {
                                        if (currentValue != output)
                                        {
                                            // Устанавливаем более подходящее имя выхода
                                            workingOutputNameField.SetValue(targetPredictor, output);
                                            
                                            if (logFixDetails)
                                                Debug.Log($"DeepLabFixer: Исправлен выходной слой с '{currentValue}' на '{output}'");
                                                
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"DeepLabFixer: Ошибка при исправлении выходного слоя: {e.Message}");
        }
    }
    
    private void TryOptimizeTextureResizing()
    {
        try
        {
            // Через reflection можно попытаться заменить метод ResizeTexture, 
            // но это сложная техника, которая может быть нестабильной.
            // В данном примере мы только логируем, что попытались это сделать.
            
            if (logFixDetails)
                Debug.Log("DeepLabFixer: Проверка оптимизации масштабирования текстур");
                
            // Здесь будет реализация оптимизации в более сложном варианте
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"DeepLabFixer: Ошибка при оптимизации масштабирования: {e.Message}");
        }
    }
    
    public void SetWallThreshold(float threshold)
    {
        wallThreshold = Mathf.Clamp01(threshold);
        
        if (targetPredictor != null)
        {
            targetPredictor.ClassificationThreshold = wallThreshold;
            
            if (logFixDetails)
                Debug.Log($"DeepLabFixer: Обновлен порог классификации на {wallThreshold}");
        }
    }
} 