using UnityEngine;
using Unity.Barracuda;
using System.Linq;
using UnityEditor;
using ML;

/// <summary>
/// Инструмент для автоматического исправления настроек SegmentationManager
/// </summary>
public class ModelFixer : MonoBehaviour
{
    public SegmentationManager segmentationManager;
    
    [Header("Автоматические настройки")]
    public bool autoFixOnAwake = true;
    public bool autoFixOutputName = true;
    public bool tryCommonOutputNames = true;
    
    // Типичные имена выходных слоев в моделях сегментации
    private readonly string[] commonOutputNames = new string[] {
        "logits", "SemanticPredictions", "output_segmentations", "output", "softmax",
        "final_output", "segmentation_output", "predictions", "sigmoid_output"
    };
    
    void Awake()
    {
        if (autoFixOnAwake && segmentationManager != null)
        {
            FixModelSettings();
        }
    }
    
    /// <summary>
    /// Автоматически настраивает параметры SegmentationManager на основе модели
    /// </summary>
    public void FixModelSettings()
    {
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager не назначен");
            return;
        }
        
        // Убедимся, что модель инициализирована
        segmentationManager.InitializeModel();
        
        // Получаем private поля через reflection
        System.Reflection.FieldInfo modelField = typeof(SegmentationManager).GetField("_runtimeModel", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        System.Reflection.FieldInfo workerField = typeof(SegmentationManager).GetField("_worker", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        System.Reflection.FieldInfo outputNameField = typeof(SegmentationManager).GetField("outputName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (modelField == null || workerField == null || outputNameField == null)
        {
            Debug.LogError("Не удалось получить доступ к приватным полям SegmentationManager");
            return;
        }
        
        Model model = (Model)modelField.GetValue(segmentationManager);
        IWorker worker = (IWorker)workerField.GetValue(segmentationManager);
        string currentOutputName = (string)outputNameField.GetValue(segmentationManager);
        
        if (model == null || worker == null)
        {
            Debug.LogWarning("Модель или worker не инициализированы");
            return;
        }
        
        Debug.Log($"Текущие выходы модели: {string.Join(", ", model.outputs)}");
        
        // Проверяем существующее имя выхода
        bool outputExists = model.outputs.Contains(currentOutputName);
        if (!outputExists && autoFixOutputName)
        {
            // Если указанный выход не существует, пробуем найти подходящий
            string newOutputName = FindWorkingOutputName(model, worker);
            if (!string.IsNullOrEmpty(newOutputName))
            {
                Debug.Log($"Изменяем имя выхода с '{currentOutputName}' на '{newOutputName}'");
                outputNameField.SetValue(segmentationManager, newOutputName);
                
                #if UNITY_EDITOR
                // Помечаем компонент как dirty в редакторе
                EditorUtility.SetDirty(segmentationManager);
                #endif
            }
        }
    }
    
    /// <summary>
    /// Ищет рабочее имя выхода из модели
    /// </summary>
    private string FindWorkingOutputName(Model model, IWorker worker)
    {
        // Сначала проверяем доступные выходы модели
        if (model.outputs.Count > 0)
        {
            foreach (string output in model.outputs)
            {
                try
                {
                    Tensor tensor = worker.PeekOutput(output);
                    Debug.Log($"Найден рабочий выход: {output}, форма: {tensor.shape}");
                    return output;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Не удалось получить тензор для выхода '{output}': {e.Message}");
                }
            }
        }
        
        // Если обычные выходы не работают, пробуем типичные имена
        if (tryCommonOutputNames)
        {
            foreach (string commonName in commonOutputNames)
            {
                try
                {
                    Tensor tensor = worker.PeekOutput(commonName);
                    Debug.Log($"Найден рабочий выход среди типичных: {commonName}, форма: {tensor.shape}");
                    return commonName;
                }
                catch (System.Exception)
                {
                    // Ничего не делаем, просто продолжаем перебор
                }
            }
        }
        
        Debug.LogError("Не удалось найти рабочее имя выхода");
        return null;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ModelFixer))]
public class ModelFixerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        ModelFixer fixer = (ModelFixer)target;
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Исправить настройки модели"))
        {
            fixer.FixModelSettings();
        }
    }
}
#endif 