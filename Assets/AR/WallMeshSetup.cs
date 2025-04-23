using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor

/// <summary>
/// Компонент для автоматической настройки AR Mesh Manager и WallMeshRenderer
/// </summary>
public class WallMeshSetup : MonoBehaviour
{
    [Header("Компоненты AR")]
    public ARCameraManager cameraManager;
    public Object wallPredictor; // Позволяет использовать как EnhancedDeepLabPredictor, так и DeepLabPredictor
    
    [Header("Настройки меша")]
    public Material wallMaterial;
    public Color wallColor = new Color(0.5f, 0.8f, 1f, 0.7f);
    public float wallConfidenceThreshold = 0.5f; // Уменьшенный порог для лучшего обнаружения
    public float verticalThreshold = 0.6f;
    public float meshUpdateInterval = 0.5f;
    
    [Header("Настройки обнаружения стен")]
    public int wallClassId = 9; // ID класса стены, должен соответствовать DeepLabPredictor.WallClassId
    public bool syncClassIdWithPredictor = true; // Синхронизировать ID класса с предиктором
    
    // Флаг для автоматической настройки при старте
    public bool setupOnStart = true;
    
    void Start()
    {
        if (setupOnStart)
        {
            SetupWallMeshRenderer();
        }
    }
    
    /// <summary>
    /// Настраивает AR Mesh Manager и добавляет WallMeshRenderer
    /// </summary>
    public void SetupWallMeshRenderer()
    {
        StartCoroutine(SetupWallMeshRoutine());
    }
    
    private IEnumerator SetupWallMeshRoutine()
    {
        // Ждем пока все компоненты будут доступны
        yield return new WaitForSeconds(0.2f);
        
        // Настраиваем wallClassId перед созданием компонентов
        SyncWallClassId();
        
        // Находим или создаем AR Mesh Manager
        GameObject arFeaturesObj = GameObject.Find("AR Features");
        if (arFeaturesObj == null)
        {
            Debug.LogWarning("AR Features объект не найден. Создаю новый...");
            GameObject xrOriginObj = GameObject.Find("XR Origin");
            if (xrOriginObj == null)
            {
                Debug.LogError("XR Origin не найден. Отмена настройки.");
                yield break;
            }
            
            arFeaturesObj = new GameObject("AR Features");
            arFeaturesObj.transform.SetParent(xrOriginObj.transform);
        }
        
        // Проверяем наличие ARPlaneManager и добавляем если нужно
        ARPlaneManager planeManager = arFeaturesObj.GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = arFeaturesObj.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            planeManager.enabled = true;
            Debug.Log("WallMeshSetup: Добавлен ARPlaneManager для обнаружения вертикальных плоскостей");
        }
        
        // Находим или создаем AR Mesh Manager
        GameObject arMeshObj = GameObject.Find("AR Mesh Manager");
        if (arMeshObj == null)
        {
            Debug.Log("Создаю новый AR Mesh Manager");
            arMeshObj = new GameObject("AR Mesh Manager");
            arMeshObj.transform.SetParent(arFeaturesObj.transform);
        }
        
        // Добавляем ARMeshManager если его нет
        ARMeshManager meshManager = arMeshObj.GetComponent<ARMeshManager>();
        if (meshManager == null)
        {
            Debug.Log("Добавляю компонент ARMeshManager");
            meshManager = arMeshObj.AddComponent<ARMeshManager>();
            meshManager.enabled = true;
        }
        
        // Добавляем WallMeshRenderer если его нет
        WallMeshRenderer wallMeshRenderer = arMeshObj.GetComponent<WallMeshRenderer>();
        if (wallMeshRenderer == null)
        {
            Debug.Log("Добавляю компонент WallMeshRenderer");
            wallMeshRenderer = arMeshObj.AddComponent<WallMeshRenderer>();
        }
        
        // Находим ARCameraManager если не указан
        if (cameraManager == null)
        {
            cameraManager = FindFirstObjectByType<ARCameraManager>();
            if (cameraManager == null)
            {
                Debug.LogWarning("ARCameraManager не найден в сцене");
            }
        }
        
        // Находим предиктор если не указан
        if (wallPredictor == null)
        {
            var enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
            if (enhancedPredictor != null)
            {
                wallPredictor = enhancedPredictor;
                Debug.Log("WallMeshSetup: Найден EnhancedDeepLabPredictor");
            }
            else
            {
                // Пробуем найти обычный DeepLabPredictor как запасной вариант
                DeepLabPredictor standardPredictor = FindFirstObjectByType<DeepLabPredictor>();
                if (standardPredictor != null)
                {
                    wallPredictor = standardPredictor;
                    Debug.Log("Найден DeepLabPredictor, используем его вместо EnhancedDeepLabPredictor");
                }
            }
        }
        
        // Настраиваем WallMeshRenderer
        wallMeshRenderer.ARCameraManager = cameraManager;
        
        // Пытаемся настроить wallPredictor
        if (wallPredictor != null)
        {
            // Cast wallPredictor to EnhancedDeepLabPredictor
            if (wallPredictor is EnhancedDeepLabPredictor enhancedPredictor)
            {
                wallMeshRenderer.Predictor = enhancedPredictor;
                Debug.Log("WallMeshSetup: Assigned EnhancedDeepLabPredictor to WallMeshRenderer");
            }
            else
            {
                Debug.LogWarning("WallMeshSetup: wallPredictor is not an EnhancedDeepLabPredictor, cannot assign to WallMeshRenderer.Predictor");
            }
            
            // Устанавливаем ID класса стены
            // Use reflection to set wallClassId since it's private
            var wallClassIdField = typeof(WallMeshRenderer).GetField("_wallClassId", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (wallClassIdField != null)
            {
                wallClassIdField.SetValue(wallMeshRenderer, wallClassId);
            }
            
            Debug.Log($"WallMeshSetup: Настроен предиктор и ID класса стены = {wallClassId}");
        }
        
        // Настраиваем материал
        if (wallMaterial != null)
        {
            wallMeshRenderer.WallMaterial = wallMaterial;
        }
        else
        {
            // Ищем материал для стен в ресурсах проекта
            Material existingWallMaterial = Resources.Load<Material>("WallMaterial");
            if (existingWallMaterial != null)
            {
                wallMeshRenderer.WallMaterial = existingWallMaterial;
            }
            else
            {
                // Пробуем найти материал по имени
                Material foundMaterial = null;
                foreach (Material mat in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (mat.name.Contains("Wall") || mat.name.Contains("wall"))
                    {
                        foundMaterial = mat;
                        break;
                    }
                }
                
                if (foundMaterial != null)
                {
                    wallMeshRenderer.WallMaterial = foundMaterial;
                }
            }
        }
        
        // Настраиваем прочие параметры
        // wallMeshRenderer._wallColor = wallColor;  
        // Create a material with the wall color
        if (wallMeshRenderer.WallMaterial != null)
        {
            wallMeshRenderer.WallMaterial.color = wallColor;
        }
        
        wallMeshRenderer.WallConfidenceThreshold = wallConfidenceThreshold;
        wallMeshRenderer.VerticalThreshold = verticalThreshold;
        
        // wallMeshRenderer._meshUpdateInterval = meshUpdateInterval;
        // Use reflection to set the update interval
        var updateIntervalField = typeof(WallMeshRenderer).GetField("_updateInterval", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (updateIntervalField != null)
        {
            updateIntervalField.SetValue(wallMeshRenderer, meshUpdateInterval);
        }
        
        // Настраиваем режим отладки для лучшей видимости проблем
        wallMeshRenderer.ShowDebugInfo = true;
        
        // Синхронизируем ссылки компонентов
        ConnectComponents();
        
        Debug.Log("WallMeshSetup: Настройка AR Mesh Manager и WallMeshRenderer завершена");
        
        // Подключаем ARPlaneManager к ARManager
        ConnectARPlaneManager();
    }
    
    /// <summary>
    /// Синхронизирует ID класса стены между компонентами
    /// </summary>
    private void SyncWallClassId()
    {
        if (!syncClassIdWithPredictor) return;
        
        // For scripts that need the wall class ID, get it from DeepLabPredictor
        DeepLabPredictor predictor = FindFirstObjectByType<DeepLabPredictor>();
        if (predictor != null)
        {
            // Try to get wallClassId through reflection
            var property = typeof(DeepLabPredictor).GetProperty("WallClassId");
            if (property != null)
            {
                try
                {
                    int predictorClassId = (int)property.GetValue(predictor);
                    wallClassId = predictorClassId;
                    Debug.Log($"WallMeshSetup: Synchronized wall class ID with DeepLabPredictor: {wallClassId}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"WallMeshSetup: Error synchronizing wall class ID: {e.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Соединяет компоненты для работы вместе
    /// </summary>
    private void ConnectComponents()
    {
        // Обновляем ссылки в WallDetectionOptimizer если он есть
        var optimizer = FindFirstObjectByType<WallDetectionOptimizer>();
        if (optimizer != null)
        {
            // Пытаемся установить правильный ID класса стены через рефлексию
            var field = typeof(WallDetectionOptimizer).GetField("wallClassId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                try
                {
                    field.SetValue(optimizer, wallClassId);
                    Debug.Log($"WallMeshSetup: Обновлен ID класса стены в WallDetectionOptimizer: {wallClassId}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"WallMeshSetup: Ошибка при обновлении WallDetectionOptimizer: {e.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Подключает ARPlaneManager к ARManager
    /// </summary>
    private void ConnectARPlaneManager()
    {
        // Находим ARManager
        ARManager arManager = FindFirstObjectByType<ARManager>();
        if (arManager == null)
        {
            Debug.LogWarning("WallMeshSetup: ARManager не найден");
            return;
        }
        
        // Находим ARPlaneManager
        ARPlaneManager planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null)
        {
            Debug.LogWarning("WallMeshSetup: ARPlaneManager не найден");
            return;
        }
        
        // Устанавливаем ссылку через рефлексию
        var field = typeof(ARManager).GetField("planeManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            try
            {
                field.SetValue(arManager, planeManager);
                Debug.Log("WallMeshSetup: Подключен ARPlaneManager к ARManager");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"WallMeshSetup: Ошибка при подключении ARPlaneManager: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Вспомогательный метод для поиска компонента в сцене
    /// </summary>
    public new static T FindFirstObjectByType<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>();
    }
} 